// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSP.Localization;
using KSP.UI;
using KSPDev.ConfigUtils;
using KSPDev.LogUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace
namespace KSPDev.LocalizationTool {

/// <summary>A utility class to manipulate the game's localization content.</summary>
static class LocalizationManager {
  /// <summary>Updates the game's localization database from the strings on the disk.</summary>
  /// <param name="configFilename">The file name with the localization data.</param>
  /// <param name="targetNode">
  /// The language node from database to update. It must not be a copy!
  /// </param>
  public static void UpdateLocalizationContent(string configFilename, ConfigNode targetNode) {
    var newNode = ConfigAccessor.GetNodeByPath(
        ConfigNode.Load(configFilename), "Localization/" + Localizer.CurrentLanguage);
    var oldTags = new HashSet<string>(targetNode.values.DistinctNames());
    var newTags = new HashSet<string>(newNode.values.DistinctNames());
    DebugEx.Warning(
        "Update localization config: added={0}, deleted={1}, updated={2}, file={3}",
        newTags.Except(oldTags).Count(),
        oldTags.Except(newTags).Count(),
        newTags.Intersect(oldTags).Count(),
        configFilename);
    // Update the existing and new tags. 
    newNode.values.Cast<ConfigNode.Value>().ToList()
        .ForEach(value => Localizer.Tags[value.name] = SafeUnescape(value.value));
    // Drop the deleted tags.
    oldTags.Except(newTags).ToList()
        .ForEach(tag => Localizer.Tags.Remove(tag));
    // Update the database config.
    targetNode.values.Clear();
    newNode.values.Cast<ConfigNode.Value>().ToList()
        .ForEach(targetNode.values.Add);
  }

  /// <summary>Updates localizable strings in the part definition.</summary>
  /// <remarks>
  /// The methods reads the current content from the part's config on disk and applies values to the
  /// localizable part fields. The up to date localization strings must already be loaded in the
  /// game before calling this method.
  /// </remarks>
  /// <param name="partInfo"></param>
  /// <seealso cref="UpdateLocalizationContent"/>
  /// <seealso cref="Extractor.LocalizablePartFields"/>
  public static void LocalizePartInfo(AvailablePart partInfo) {
    var newPartConfig = GetPartPrefabConfig(partInfo);
    if (newPartConfig == null) {
      return;
    }

    DebugEx.Info("Update strings in part {0}", partInfo.partPrefab);
    Extractor.LocalizablePartFields.ToList().ForEach(name => {
      var newValue = newPartConfig.GetValue(name);
      if (newValue != null) {
        ReflectionHelper.SetReflectedString(partInfo, name, newValue);
      }
    });

    // Update the prefab module info.
    // This is a simplified algorithm of the part localization. It may not work for all the cases.
    var partModules = partInfo.partPrefab.Modules.GetModules<PartModule>()
        .Where(x => !string.IsNullOrEmpty(x.GetInfo().Trim()))
        .ToList();
    if (partModules.Count > partInfo.moduleInfos.Count) {
      // When modules are added to prefab after the database load, the count can mismatch.
      // Those extra modules will be skipped during the refresh since they are not visible anyway.
      DebugEx.Warning(
          "Part {0} has {1} UI visible modules, but there only {2} module infos",
          partInfo.name, partModules.Count, partInfo.moduleInfos.Count);
    } else if (partInfo.moduleInfos.Count > partModules.Count) {
      // Can happen when a module is deleted in runtime. Such modules will get lost in refresh.
      DebugEx.Warning(
          "Part {0} has {1} module infos, but there are only {2} UI visible modules",
          partInfo.name, partInfo.moduleInfos.Count, partModules.Count);
    }
    for (var i = 0; i < partInfo.moduleInfos.Count && i < partModules.Count; i++) {
      var moduleInfo = partInfo.moduleInfos[i];
      var partModule = partModules[i];
      var partModuleInfo = partModule as IModuleInfo;
      if (partModuleInfo != null) {
        moduleInfo.moduleName = partModuleInfo.GetModuleTitle();
        moduleInfo.primaryInfo = partModuleInfo.GetPrimaryField();
      }
      moduleInfo.info = partModule.GetInfo().Trim();
      moduleInfo.moduleDisplayName = partModule.GetModuleDisplayName();
      if (moduleInfo.moduleDisplayName == "") {
        moduleInfo.moduleDisplayName = moduleInfo.moduleName;
      }
    }
  }

  /// <summary>Updates data in all the open part menus.</summary>
  public static void LocalizePartMenus() {
    // The editor's tooltip caches the data, and we cannot update it. So just reset it.
    if (HighLogic.LoadedSceneIsEditor) {
      UIMasterController.Instance.DestroyCurrentTooltip();
    }
    UnityEngine.Object.FindObjectsOfType(typeof(UIPartActionWindow))
        .OfType<UIPartActionWindow>()
        .ToList()
        .ForEach(m => {
          DebugEx.Info("Localize menu for part {0}", m.part);
          m.titleText.text = m.part.partInfo.title;
          m.displayDirty = true;
        });
  }

  /// <summary>Localizes the values in the part's prefab config.</summary>
  /// <param name="partInfo">The p[art info to localize.</param>
  public static void LocalizePrefab(AvailablePart partInfo) {
    var newPartConfig = GetPartPrefabConfig(partInfo);
    if (newPartConfig == null) {
      return;
    }
    var newModuleConfigs = newPartConfig.GetNodes("MODULE");
    var prefabModuleConfigs = partInfo.partConfig.GetNodes("MODULE");
    if (newModuleConfigs.Length <= prefabModuleConfigs.Length) {
      // Due to the ModuleManager patches, the prefab config may have more modules than the
      // disk version.
      for (var i = 0; i < newModuleConfigs.Length; i++) {
        var newConf = newModuleConfigs[i];
        var prefabConf = prefabModuleConfigs[i];
        if (newConf.GetValue("name") == prefabConf.GetValue("name")) {
          MergeLocalizableValues(prefabConf, newConf);
        } else {
          DebugEx.Warning("Skipping module on part {0}: newName={1}, prefabName={2}",
                          partInfo.name, newConf.GetValue("name"), prefabConf.GetValue("name"));
        }
        // Reload all KSPField strings in the module to get the changed version.
        if (i < partInfo.partPrefab.Modules.Count) {
          LoadKspFieldsFromNode(partInfo.partPrefab.Modules[i], prefabConf);
        } else {
          DebugEx.Error(
              "Cannot reload strings in {0}: module #{1} not found", partInfo.partPrefab, i);
        }
      }
    } else {
      // MM patches can delete modules, but this is not supported.
      DebugEx.Error(
          "Cannot refresh part config fields in part {0}. Config file has more modules than the"
          + " prefab: in file={1}, in prefab={2}",
          partInfo.name, newModuleConfigs.Length, prefabModuleConfigs.Length);
    }
  }

  /// <summary>Updates the modules in all the current vessels or parts in the scene.</summary>
  public static void ReloadPartModuleStrings(HashSet<string> selectedParts = null) {
    // FLIGHT: Update the part modules in all the loaded vessels.
    if (HighLogic.LoadedSceneIsFlight) {
      DebugEx.Info("FLIGHT: Reload parts on the vessels...");
      FlightGlobals.Vessels
          .Where(v => v.loaded)
          .SelectMany(v => v.parts)
          .Where(p => selectedParts == null || selectedParts.Contains(p.partInfo.name))
          .ToList()
          .ForEach(UpdateLocalizationInPartModules);
    }

    // EDITOR: Update the part modules in all the game objects in the scene.
    // ReSharper disable once InvertIf
    if (HighLogic.LoadedSceneIsEditor) {
      DebugEx.Info("EDITOR: Reload parts in the world...");
      // It can be slow but we don't care - it's not a frequent operation.
      UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
          .Select(o => o.GetComponent<Part>())
          .Where(p => p != null)
          .ToList()
          .ForEach(p => UpdateLocalizationInPartHierarchy(p, selectedParts));
    }
  }

  /// <summary>Checks if the text is a possible localization tag.</summary>
  /// <param name="txt">
  /// The text to check for the tag candidate. It can be empty or <c>null</c>.
  /// </param>
  /// <param name="firstWordOnly">
  /// Tells if only the first word of the text should be checked.
  /// </param>
  /// <returns><c>true</c> if the text or its first word looks like a localization tag.</returns>
  public static bool IsLocalizationTag(string txt, bool firstWordOnly = false) {
    if (string.IsNullOrEmpty(txt)) {
      return false;
    }
    if (firstWordOnly) {
      var matches = Regex.Match(txt, @"^.*(\w){1}");
      if (!matches.Success) {
        return false;
      }
      txt = matches.Groups[1].Value;
    }
    return txt.StartsWith("#", StringComparison.Ordinal)
        && !Regex.IsMatch(txt, @"^#[0-9a-fA-F]{6}$")
        && !Regex.IsMatch(txt, @"^#[0-9a-fA-F]{3}$");
  }

  /// <summary>Checks if localization tag must be ignored in export.</summary>
  /// <param name="txt">The tag text.</param>
  /// <returns><c>true</c> if the tag must be skipped.</returns>
  public static bool IsSkippedTag(string txt) {
    return Controller.skipTags.Any(txt.StartsWith);
  }

  /// <summary>Merges localizable values from one config node to another.</summary>
  /// <remarks>
  /// The values in the nodes must be in the same order. The <paramref name="toNode"/> is allowed
  /// to have more values, the extra values will be silently skipped.
  /// </remarks>
  /// <param name="toNode">The node to merge value to.</param>
  /// <param name="fromNode">The node to merge values from. It must have comments loaded.</param>
  static void MergeLocalizableValues(ConfigNode toNode, ConfigNode fromNode) {
    for (var i = 0; i < fromNode.values.Count && i < toNode.values.Count; i++) {
      var fromValue = fromNode.values[i];
      var toValue = toNode.values[i];
      if (fromValue.name != toValue.name) {
        DebugEx.Error("Cannot merge config nodes.\nTO:\n{0}\nFROM:\n{1}", toNode, fromNode);
        return;
      }
      if (IsLocalizationTag(fromValue.comment, firstWordOnly: true)) {
        toValue.value = fromValue.value;
        toValue.comment = fromValue.comment;
      }
    }
    for (var i = 0; i < fromNode.nodes.Count && i < toNode.nodes.Count; i++) {
      MergeLocalizableValues(toNode.nodes[i], fromNode.nodes[i]);
    }
  }

  /// <summary>Reads part's config file from file for the requested part.</summary>
  /// <remarks>
  /// It loads the config with the inline comments. For the multi-part configs the right node is
  /// located. If the config has MM patches for the "PART" section, then they are dropped, but a
  /// warning is logged.
  /// </remarks>
  /// <param name="partInfo">The part to get config for.</param>
  /// <returns>The config or <c>null</c> if config cannot be found or loaded.</returns>
  static ConfigNode GetPartPrefabConfig(AvailablePart partInfo) {
    if (string.IsNullOrEmpty(partInfo.configFileFullName)) {
      DebugEx.Error("Skip part {0} since it doesn't have a config", partInfo.name);
      return null;
    }
    var config = ConfigStore.LoadConfigWithComments(
        partInfo.configFileFullName, skipLineComments: true);
    if (config == null || config.nodes.Count == 0) {
      DebugEx.Error("Config node is invalid for the part {0}\n{1}", partInfo.name, config);
      return null;
    }
    ConfigNode result = null;
    for (var i = 0; i < config.nodes.Count; i++) {
      var node = config.nodes[i];
      if (node.name != "PART") {
        DebugEx.Warning("Non-part node in config of part {0}: {1}", partInfo.name, node.name);
        continue;
      }
      // KSP mangles with the part names translating "." to "_" and back. So just in case do it on
      // the both sides.
      if (node.GetValue("name").Replace(".", "_") != partInfo.name.Replace(".", "_")) {
        DebugEx.Warning("Node in config of part '{0}' doesn't match the part name: '{1}'",
                        partInfo.name, node.GetValue("name"));
        continue;
      }
      if (result == null) {
        result = node;
      } else {
        DebugEx.Warning(
            "Skipping node #{0} in config of part {1} due to duplication", i, partInfo.name);
      }
    }
    return result;
  }

  /// <summary>Localizes the modules in the part and in all of its children parts.</summary>
  /// <param name="rootPart">The root part to start from.</param>
  /// <param name="selectedParts">
  /// The names of the parts to update. If <c>null</c>, then update all.
  /// </param>
  static void UpdateLocalizationInPartHierarchy(Part rootPart, ICollection<string> selectedParts) {
    if (selectedParts == null || selectedParts.Contains(rootPart.partInfo.name)) {
      UpdateLocalizationInPartModules(rootPart);
    }
    rootPart.children.ForEach(p => UpdateLocalizationInPartHierarchy(p, selectedParts));
  }

  /// <summary>Updates all the localizable strings in a part.</summary>
  /// <param name="part">The part to load the data in.</param>
  static void UpdateLocalizationInPartModules(Part part) {
    if (part.partInfo?.partConfig == null) {
      return;
    }
    DebugEx.Fine("Reload part modules in {0}...", part);
    var moduleConfigs = part.partInfo.partConfig.GetNodes("MODULE");
    for (var i = 0 ; i < part.Modules.Count && i < moduleConfigs.Length; i++) {
      var module = part.Modules[i];
      var moduleConfig = moduleConfigs[i];
      LoadKspFieldsFromNode(module, moduleConfig);
    }
  }

  /// <summary>Reloads the string [KSPField] annotated fields from the provided config.</summary>
  /// <param name="module">The module to reload the fields for.</param>
  /// <param name="node">The config node to get the values from.</param>
  static void LoadKspFieldsFromNode(PartModule module, ConfigNode node) {
    // Update all fields of type string as they may contain a localizable content. 
    var stringFields = module.Fields.Cast<BaseField>()
        .Where(f => f.FieldInfo.FieldType == typeof(string));
    foreach (var field in stringFields) {
      var strValue = node.GetValue(field.name);
      if (strValue != null) {
        field.SetValue(strValue, module);
      }
    }
  }

  /// <summary>Unescapes the string even if it has escaping errors.</summary>
  static string SafeUnescape(string srcValue) {
    try {
      return Regex.Unescape(srcValue);
    } catch (Exception ex) {
      DebugEx.Error(
          "Cannot properly unescape value, falling back to a simple approach: err={0}, value={1}",
          ex.Message, srcValue);
    }
    return srcValue.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\t", "\t");
  }
}

}  // namespace
