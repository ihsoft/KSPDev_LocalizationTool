﻿// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSPDev.GUIUtils;
using KSPDev.LogUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KSPDev.LocalizationTool {

static class Extractor {
  /// <summary>List of the part's fields that need localization.</summary>
  public readonly static string[] localizablePartFields =
      {"title", "manufacturer", "description", "tags"};

  /// <summary>Extracts the localization items from the part's config.</summary>
  /// <param name="part">The part to extract items for.</param>
  /// <returns>All the localization items for the part.</returns>
  public static List<LocItem> EmitItemsForPart(AvailablePart part) {
    var res = new List<LocItem>();
    // The part's config in the AvailablePart doesn't have all the fields and lacks the comments.
    // We do need the comments to resolve the field tag names, so load via a custom method.
    // In case of the custom loading method fails, use the stock one without the comments support.
    var config = (ConfigStore.LoadConfigWithComments(part.configFileFullName)
                  ?? ConfigNode.Load(part.configFileFullName)).GetNode("PART");
    if (config == null) {
      DebugEx.Error("Failed to load part's config: partName={0}, configUrl={1}",
                    part.name, part.configFileFullName);
      return res;
    }

    // Go thru the fields we know must be localized.
    foreach (var fieldName in localizablePartFields) {
      var field = config.values.Cast<ConfigNode.Value>()
          .FirstOrDefault(x => x.name == fieldName);
      if (field == null) {
        DebugEx.Warning("Field '{0}' is not found in the part {1} config",
                        fieldName, part.name);
        continue;
      }
      config.values.Remove(field);  // Don't handle it down the stream.
      string locTag = null;
      string locDefaultValue = null;
      if (!string.IsNullOrEmpty(field.comment)
          && field.comment.StartsWith("#", StringComparison.Ordinal)) {
        var match = Regex.Match(field.comment, @"^(#[a-zA-Z0-9_-]+)\s*=\s*(.+?)$");
        if (match.Success) {
          locTag = match.Groups[1].Value;
          if (field.value == locTag) {
            // Part's tag localization failed, use the default text.
            locDefaultValue = match.Groups[2].Value; 
          }
        } else {
          DebugEx.Warning(
              "Cannot resolve defult localization tag in field {0} for part {1}: {2}",
              fieldName, config.GetValue("name"), field.comment);
        }
      }
      var item = new LocItem() {
          groupKey = "Part: " + part.name,
          fullFilePath = part.configFileFullName,
          locTag = locTag ?? MakePartFieldLocalizationTag(config.GetValue("name"), fieldName),
          locDefaultValue = locDefaultValue ?? field.value,
      };
      res.Add(item);
    }

    res.AddRange(EmitItemsForNode(part, config));

    return res;
  }

  /// <summary>Extracts strings from the specified type.</summary>
  /// <remarks>
  /// The method can extract strings from the members annotated by the KSP attributes:
  /// <see cref="KSPField"/>, <see cref="KSPEvent"/>, and <see cref="KSPAction"/>. It also supports
  /// <c>KSPDev</c> localization class <see cref="KSPDev.GUIUtils.LocalizableMessage"/>.
  /// </remarks>
  /// <param name="type"></param>
  /// <returns></returns>
  public static List<LocItem> EmitItemsForType(Type type) {
    var res = new List<LocItem>();
    var members = type.GetMembers(
        BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly);
    foreach (var member in members) {
      var memberItems = new List<LocItem>();
      if (memberItems.Count == 0) {
        memberItems = EmitItemsForLocalizableMessage(member);
      }
      if (memberItems.Count == 0) {
        memberItems = EmitItemsForKSPField(member);
      }
      if (memberItems.Count == 0) {
        memberItems = EmitItemsForKSPEvent(member);
      }
      if (memberItems.Count == 0) {
        memberItems = EmitItemsForKSPAction(member);
      }
      res.AddRange(memberItems);
    }
    return res;
  }

  /// <summary>Makes a tag for the part field.</summary>
  /// <param name="partName">The name of the part to make a tag for.</param>
  /// <param name="fieldName">The field name in the config.</param>
  /// <returns>A complete and correct localization tag.</returns>
  public static string MakePartFieldLocalizationTag(string partName, string fieldName) {
    return "#" + partName.Replace(".", "_") + "_Part_" + fieldName;
  }


  /// <summary>Makes a tag for the type member.</summary>
  /// <param name="info">The member to make a tag for.</param>
  /// <param name="nameSuffix">
  /// A string to add at the tag end to disambiguate the otherwise identical strings.
  /// </param>
  /// <returns>A complete and correct localization tag.</returns>
  public static string MakeTypeMemberLocalizationTag(MemberInfo info, string nameSuffix = "") {
    return "#" + info.DeclaringType.FullName.Replace(".", "_") + "_" + info.Name + nameSuffix;
  }

  #region Local utility methods
  /// <summary>Extracts localization items from the <c>[KSPField]</c> annotated fields.</summary>
  /// <param name="info">The type member to extract the strings for.</param>
  /// <returns>All the localization items for the member.</returns>
  static List<LocItem> EmitItemsForKSPField(MemberInfo info) {
    var res = new List<LocItem>();
    var attrObj = info.GetCustomAttributes(false).OfType<KSPField>().FirstOrDefault();
    if (attrObj == null) {
      return res;
    }

    var groupKey = "Type: " + info.DeclaringType.FullName;
    const string subgroupKey = "KSP Fields";
    // Get guiName localization.
    var guiNameLoc = GetItemFromLocalizableObject(info, groupKey, subgroupKey);
    if (!guiNameLoc.HasValue && !string.IsNullOrEmpty(attrObj.guiName)) {
      // Fallback to the KSPField values.
      guiNameLoc = new LocItem() {
          groupKey = groupKey,
          subgroupKey = subgroupKey,
          fullFilePath = info.DeclaringType.Assembly.Location,
          locTag = MakeTypeMemberLocalizationTag(info),
          locDefaultValue = attrObj.guiName,
      };
    }
    if (guiNameLoc.HasValue) {
      res.Add(guiNameLoc.Value);
    }

    // Get localization for the units if present.
    var guiUnitsLoc = GetItemFromLocalizableObject(
        info, groupKey, subgroupKey, spec: StdSpecTags.Units);
    if (!guiUnitsLoc.HasValue && !string.IsNullOrEmpty(attrObj.guiUnits)) {
      // Fallback to the KSPField values.
      guiUnitsLoc = new LocItem() {
          groupKey = groupKey,
          subgroupKey = subgroupKey,
          fullFilePath = info.DeclaringType.Assembly.Location,
          locTag = MakeTypeMemberLocalizationTag(info, nameSuffix: "_Units"),
          locDefaultValue = attrObj.guiUnits,
      };
    }
    if (guiUnitsLoc.HasValue) {
      res.Add(guiUnitsLoc.Value);
    }

    return res;
  }

  /// <summary>Extracts localization items from the <c>[KSPEvent]</c> annotated fields.</summary>
  /// <param name="info">The type member to extract the strings for.</param>
  /// <returns>All the localization items for the member.</returns>
  static List<LocItem> EmitItemsForKSPEvent(MemberInfo info) {
    var res = new List<LocItem>();
    var attrObj = info.GetCustomAttributes(false).OfType<KSPEvent>().FirstOrDefault();
    if (attrObj != null) {
      var groupKey = "Type: " + info.DeclaringType.FullName;
      const string sortKey = "KSP Events";
      // Get guiName localization.
      var guiNameLoc = GetItemFromLocalizableObject(info, groupKey, sortKey);
      if (!guiNameLoc.HasValue && !string.IsNullOrEmpty(attrObj.guiName)) {
        // Fallback to the KSPEvent values.
        guiNameLoc = new LocItem() {
            groupKey = groupKey,
            subgroupKey = sortKey,
            fullFilePath = info.DeclaringType.Assembly.Location,
            locTag = MakeTypeMemberLocalizationTag(info),
            locDefaultValue = attrObj.guiName,
        };
      }
      if (guiNameLoc.HasValue) {
        res.Add(guiNameLoc.Value);
      }
    }
    return res;
  }

  /// <summary>Extracts localization items from the <c>[KSPAction]</c> annotated fields.</summary>
  /// <param name="info">The type member to extract the strings for.</param>
  /// <returns>All the localization items for the member.</returns>
  static List<LocItem> EmitItemsForKSPAction(MemberInfo info) {
    var res = new List<LocItem>();
    var attrObj = info.GetCustomAttributes(false).OfType<KSPAction>().FirstOrDefault();
    if (attrObj != null) {
      var groupKey = "Type: " + info.DeclaringType.FullName;
      const string sortKey = "KSP Actions";
      // Get guiName localization.
      var guiNameLoc = GetItemFromLocalizableObject(info, groupKey, sortKey);
      if (!guiNameLoc.HasValue && !string.IsNullOrEmpty(attrObj.guiName)) {
        // Fallback to the KSPAction values.
        guiNameLoc = new LocItem() {
            groupKey = groupKey,
            subgroupKey = sortKey,
            fullFilePath = info.DeclaringType.Assembly.Location,
            locTag = MakeTypeMemberLocalizationTag(info),
            locDefaultValue = attrObj.guiName,
        };
      }
      if (guiNameLoc.HasValue) {
        res.Add(guiNameLoc.Value);
      }
    }
    return res;
  }

  /// <summary>
  /// Extracts localization info from a <see cref="KSPDev.GUIUtils.LocalizableMessage"/> classes.
  /// </summary>
  /// <param name="info">The type member to extract the strings for.</param>
  /// <returns>All the localization items for the member.</returns>
  static List<LocItem> EmitItemsForLocalizableMessage(MemberInfo info) {
    var res = new List<LocItem>();
    var field = info as FieldInfo;
    if (field == null
        || !ReflectionHelper.CheckReflectionParent(field.FieldType,
                                                  "KSPDev.GUIUtils.LocalizableMessage")) {
      return res;
    }
    if ((field.Attributes & FieldAttributes.Static) == 0) {
      DebugEx.Warning("Skipping a non-static message field: {0}.{1}",
                      field.DeclaringType.FullName, field.Name);
      return res;
    }
    var value = field.GetValue(null);
    if (value == null) {
      DebugEx.Error("The message field is NULL: {0}.{1}", field.DeclaringType.FullName, field.Name);
      return res;
    }
    var msgTag = ReflectionHelper.GetReflectedString(value, "tag") ?? "";
    var defaultTemplate = ReflectionHelper.GetReflectedString(value, "defaultTemplate");
    if (defaultTemplate == null) {
      // The template is never null.
      DebugEx.Warning("Failed to read a message from {0} in {1}.{2}",
                      field.FieldType.FullName,
                      field.DeclaringType.FullName, field.Name);
      return res;
    }
    var description = ReflectionHelper.GetReflectedString(value, "description");
    var locExample = ReflectionHelper.GetReflectedString(value, "example");
    if (!msgTag.StartsWith("#", StringComparison.Ordinal)) {
      msgTag = MakeTypeMemberLocalizationTag(info);
      DebugEx.Warning("Auto generate a tag {0}", msgTag);
    }
    res.Add(new LocItem() {
        groupKey = "Type: " + info.DeclaringType.FullName,
        subgroupKey = "KSPDev Messages",
        fullFilePath = field.FieldType.Assembly.Location,
        locTag = msgTag,
        locDefaultValue = defaultTemplate,
        locDescription = description,
        locExample = locExample,
    });
    return res;
  }

  /// <summary>
  /// Extarcts localization information from a member attributed with a KSPDev loсalization
  /// attribute.
  /// </summary>
  /// <param name="info">The member to extract the attribute for.</param>
  /// <param name="groupKey">The group key to apply to the item if it's found.</param>
  /// <param name="sortKey">The sort key to apply to the item if it's found.</param>
  /// <param name="spec">The specialization string to pick the attribute.</param>
  /// <returns>
  /// A localization item or <c>null</c> if no attrribute was found, or if the attribute is
  /// explicilty specifying that there is no localization information for the member
  /// (tag = <c>null</c>).
  /// </returns>
  static LocItem? GetItemFromLocalizableObject(
      MemberInfo info, string groupKey, string sortKey, string spec = null) {
    var attrObj = info.GetCustomAttributes(false)
        .FirstOrDefault(o =>
            ReflectionHelper.CheckReflectionParent(
                o.GetType(), "KSPDev.GUIUtils.LocalizableItemAttribute")
            && ReflectionHelper.GetReflectedString(o, "spec") == spec);
    if (attrObj == null) {
      return null;
    }
    var locTag = ReflectionHelper.GetReflectedString(attrObj, "tag");
    if (string.IsNullOrEmpty(locTag)) {
      return null;  // The item is explicitly saying there is no localization.
    }
    return new LocItem() {
        groupKey = groupKey,
        subgroupKey = sortKey,
        fullFilePath = info.DeclaringType.Assembly.Location,
        locTag = locTag,
        locDefaultValue = ReflectionHelper.GetReflectedString(attrObj, "defaultTemplate"),
        locDescription = ReflectionHelper.GetReflectedString(attrObj, "description"),
    };
  }

  /// <summary>Extracts the strings that look like the localized tags.</summary>
  /// <remarks>
  /// This methods looks for the values that are localized in way it's done in the stock parts.
  /// </remarks>
  /// <param name="part">The part to extract the items for.</param>
  /// <param name="config">
  /// The config node of the part. It's an extend version with the comments.
  /// </param>
  /// <returns>The list of extracted items.</returns>
  static List<LocItem> EmitItemsForNode(AvailablePart part, ConfigNode config) {
    var res = new List<LocItem>();

    // Go thru all the fields to detect if there are localized optional fields.
    foreach (var field in config.values.Cast<ConfigNode.Value>()) {
      if (string.IsNullOrEmpty(field.comment)) {
        continue;
      }
      var match = Regex.Match(field.comment, @"^\s*(#[a-zA-Z0-9_-]+)\s*=\s*(.+?)$");
      if (!match.Success) {
        continue;  // Not localized.
      }
      var locTag = match.Groups[1].Value;
      var locValue = field.value;
      if (field.value == locTag) {
        // In case of the tag is not get resolved, use the default template. 
        DebugEx.Warning(
            "Field '{0}' in part {1} looks localized, but the tag '{2}' is not found"
            + " (suggested default value: '{3}')",
            field.name, part.name, locTag, match.Groups[2].Value);
        locValue = match.Groups[2].Value;
      }
      var item = new LocItem() {
          groupKey = "Part: " + part.name,
          fullFilePath = part.configFileFullName,
          locTag = locTag,
          locDefaultValue = locValue,
      };
      res.Add(item);
    }

    // Scan the nested nodes.
    foreach (var nestedNode in config.GetNodes()) {
      res.AddRange(EmitItemsForNode(part, nestedNode));
    }
    
    return res;
  }
  #endregion
}

}  // namesapce
