﻿// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSP.Localization;
using KSPDev.LogUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSPDev.LocalizationTool {

static class ConfigStore {
  /// <summary>Writes the localization items into file.</summary>
  /// <remarks>
  /// <list type="bullet">
  /// <item>All items are grouped by <see cref="LocItem.groupKey"/>.</item>
  /// <item>The groups are sorted in the ascending order.</item>
  /// <item>The items within a group are sorted in the ascending order.</item>
  /// <item>
  /// If item has <see cref="LocItem.locDescription"/>, then its written as a comment that precedes
  /// the tag-to-value line.
  /// </item>
  /// </list>
  /// </remarks>
  /// <param name="items">The items to write.</param>
  /// <param name="lang">
  /// The language of the file. It will be used for the language block in the file.
  /// </param>
  /// <param name="filePath">The file path to write the data into.</param>
  /// <param name="localizeValues">
  /// Tells if the original template or the localized values should be saved.
  /// </param>
  public static void WriteLocItems(IEnumerable<LocItem> items, string lang, string filePath,
                                   bool localizeValues = true) {
    using (var file = new StreamWriter(filePath)) {
      file.WriteLine("// Auto generated by KSPDev Localization tool at: " + DateTime.Now);
      var locItems = items as LocItem[] ?? items.ToArray();
      file.WriteLine("// Total strings: " + locItems.Length);
      var wordsCount = locItems
          .Where(x => x.locDefaultValue != null && Regex.IsMatch(x.locDefaultValue, @"\w+"))
          .Sum(x => Regex.Matches(x.locDefaultValue, @"\w+").Count);
      file.WriteLine("// Total words: " + wordsCount);
      
      // Report duplicated tags if any.
      var duplicates = locItems
          .Where(x =>
              !Controller.skipTags.Any(x.locTag.StartsWith)
              && !Controller.globalPrefix.Any(x.locTag.StartsWith))
          .GroupBy(x => x.locTag)
          .Select(x => new { tag = x.Key, count = x.Count() })
          .Where(x => x.count > 1);
      foreach (var duplicate in duplicates) {
        file.WriteLine("// DUPLICATED TAG: " + duplicate.tag);
        DebugEx.Warning("Found duplicated tag: {0}", duplicate.tag);
      }

      file.Write("Localization\n{\n\t" + lang + "\n\t{\n");

      // Tags, acquired from parts and DLLs.
      var byGroupKey = locItems
          .Where(x =>
              !Controller.skipTags.Any(x.locTag.StartsWith)
              && !Controller.globalPrefix.Any(x.locTag.StartsWith))
          .OrderBy(x => x.groupKey)
          .ThenBy(x => x.subgroupKey)
          .ThenBy(x => string.IsNullOrEmpty(x.sortKey) ? "\0xff" : x.sortKey)
          .ThenBy(x => x.locTag)
          .GroupBy(x => new { x.groupKey, x.subgroupKey });
      foreach (var groupKeyItems in byGroupKey) {
        var groupText = !string.IsNullOrEmpty(groupKeyItems.Key.subgroupKey)
            ? groupKeyItems.Key.groupKey + ", " + groupKeyItems.Key.subgroupKey
            : groupKeyItems.Key.groupKey;
        file.WriteLine("\n\t\t// ********** " + groupText + "\n");
        foreach (var item in groupKeyItems) {
          if (!string.IsNullOrEmpty(item.locDescription)) {
            file.WriteLine(MakeMultilineComment(2, item.locDescription, maxLineLength: 100 - 2*8));
          }
          if (!string.IsNullOrEmpty(item.locExample)) {
            file.WriteLine(MakeMultilineComment(2, "Example usage:"));
            file.WriteLine(MakeMultilineComment(2, item.locExample));
          }
          if (localizeValues) {
            string localizedValue;
            file.WriteLine(
                Localizer.TryGetStringByTag(item.locTag, out localizedValue)
                    ? MakeConfigNodeLine(2, item.locTag, localizedValue)
                    : MakeConfigNodeLine(2, item.locTag, item.locDefaultValue));
          } else {
            file.WriteLine(MakeConfigNodeLine(2, item.locTag, item.locDefaultValue));
          }
        }
      }

      // Global tags. They can appear multiple times in DLL or part CFG.
      // Only use description if it's not null.
      var globalTags = locItems
          .Where(x => Controller.globalPrefix.Any(x.locTag.StartsWith))
          .OrderBy(x => x.locTag)
          .ThenBy(x => x.fullFilePath)
          .GroupBy(x => new { x.locTag });
      file.WriteLine("\n\t\t// ********** Global strings\n");
      foreach (var globalStringsGroup in globalTags) {
        var bestItem = default(LocItem);
        foreach (var item in globalStringsGroup) {
          file.WriteLine("\t\t// Referenced from: " + item.fullFilePath);
          if (item.locDescription != null && bestItem.locTag != "") {
            bestItem = item;
          }
        }
        if (bestItem.locTag == null) {
          DebugEx.Warning("Global tag doesn't provide any description: tag={0}",
                          globalStringsGroup.Key.locTag);
          bestItem = globalStringsGroup.First();
        }
        if (!string.IsNullOrEmpty(bestItem.locDescription)) {
          file.WriteLine(MakeMultilineComment(2, bestItem.locDescription, maxLineLength: 100 - 2*8));
        }
        if (!string.IsNullOrEmpty(bestItem.locExample)) {
          file.WriteLine(MakeMultilineComment(2, "Example usage:"));
          file.WriteLine(MakeMultilineComment(2, bestItem.locExample));
        }
        if (localizeValues) {
          string localizedValue;
          file.WriteLine(
              Localizer.TryGetStringByTag(bestItem.locTag, out localizedValue)
                  ? MakeConfigNodeLine(2, bestItem.locTag, localizedValue)
                  : MakeConfigNodeLine(2, bestItem.locTag, bestItem.locDefaultValue));
        } else {
          file.WriteLine(MakeConfigNodeLine(2, bestItem.locTag, bestItem.locDefaultValue));
        }
      }

      file.Write("\t}\n}\n");
    }
  }

  /// <summary>Loads a config file preserving the value comments.</summary>
  /// <param name="fileFullName">The file to load.</param>
  /// <param name="localizeValues">
  /// Tells if the field values should be localized. If not, then they are returned from the file
  /// "as-is".
  /// </param>
  /// <returns>A loaded config node.</returns>
  /// <seealso cref="PartConfigParser"/>
  public static ConfigNode LoadConfigWithComments(string fileFullName, bool localizeValues = true) {
    var parser = new PartConfigParser(localizeValues: localizeValues);
    return parser.ParseFileAsNode(fileFullName);
  }

  /// <summary>Saves the node into a config file preserving the comments.</summary>
  /// <remarks>It's a counter part to the <see cref="LoadConfigWithComments"/> method.</remarks>
  /// <param name="node">The node with the comments to save.</param>
  /// <param name="path">
  /// The path to save at. The missing directions in the path will be created if missed.
  /// </param>
  public static void SaveConfigWithComments(ConfigNode node, string path) {
    var content = new StringBuilder();
    foreach (var childNode in node.GetNodes()) {
      SerializeNode(content, childNode , 0);
    }
    var savePath = Path.GetDirectoryName(path);
    if (savePath == null) {
      throw new ArgumentException("Path is not valid for export: {0}", path);
    }
    Directory.CreateDirectory(savePath);
    File.WriteAllText(path, content.ToString());
  }

  #region Local utility methods
  /// <summary>Recursively collects and serializes the fields in the nodes.</summary>
  /// <remarks>
  /// Supports special field name <c>__commentField</c> to output the line comments. If the line
  /// comment is empty, then only an empty line is output into the result.
  /// </remarks>
  /// <param name="res"></param>
  /// <param name="node"></param>
  /// <param name="indentation"></param>
  static void SerializeNode(StringBuilder res, ConfigNode node, int indentation) {
    var indentSpaces = new string('\t', indentation);
    var nodeMeta = MetaBlock.MakeFromString(node.comment);
    var nodeOpenText = indentSpaces + node.name;
    if (nodeMeta.inlineComment != null) {
      nodeOpenText += " // " + nodeMeta.inlineComment;  
    }
    res.AppendLine(nodeOpenText);
    var openBlockText = indentSpaces + "{";
    if (nodeMeta.openBlockComment != null) {
      openBlockText += " // " + nodeMeta.openBlockComment;
    }
    res.AppendLine(openBlockText);
    indentation++;
    var indentBlock = indentSpaces + '\t'; 

    var fields = node.values.Cast<ConfigNode.Value>();
    foreach (var field in fields) {
      var meta = MetaBlock.MakeFromString(field.comment);
      foreach (var line in meta.trailingLines) {
        if (line.isEmptyLine) {
          res.AppendLine("");
        } else {
          res.AppendLine(indentBlock + "// " + line.value);
        }
      }
      if (meta.isFakeField) {
        continue; // No actual field is needed.
      }
      res.AppendLine(MakeConfigNodeLine(indentation, field.name, field.value, meta: meta));
    }
    if (node.CountNodes > 0) {
      res.AppendLine("");
      foreach (var childNode in node.GetNodes()) {
        SerializeNode(res, childNode, indentation);
      }
    }
    var nodeCloseText = indentSpaces + "}";
    if (nodeMeta.closeBlockComment != null) {
      nodeCloseText += " // " + nodeMeta.closeBlockComment;  
    }
    res.AppendLine(nodeCloseText);
  }

  /// <summary>Formats a comment with the proper indentation.</summary>
  /// <param name="indentation">The indentation in tabs. Each tab is 8 spaces.</param>
  /// <param name="comment">
  /// The comment to format. It can contain multiple lines separated by a "\n" symbols.
  /// </param>
  /// <param name="maxLineLength">
  /// A maximum length of the line in the file. If the comment exceeds this limit, then it's
  /// wrapped.
  /// </param>
  /// <returns>A properly formatted comment block.</returns>
  static string MakeMultilineComment(int indentation, string comment, int? maxLineLength = null) {
    return string.Join(
        "\n",
        comment.Split('\n')
            .SelectMany(l => WrapLine(l, maxLineLength - 3))  // -3 for the comment.
            .Select(x => new string('\t', indentation) + "// " + x).ToArray());
  }

  /// <summary>Wraps the line so that each item's length is not greater than the limit.</summary>
  /// <remarks>This method doesn't recognize any special symbols like tabs or line feeds.</remarks>
  /// <param name="line">The line to wrap.</param>
  /// <param name="maxLength">The maximum length of each item.</param>
  /// <returns>A list of line items.</returns>
  static List<string> WrapLine(string line, int? maxLength) {
    var lines = new List<string>();
    if (!maxLength.HasValue) {
      lines.Add(line);
      return lines;
    }
    var wordMatch = Regex.Match(line.Trim(), @"(\s*\S+\s*?)");
    var currentLine = "";
    while (wordMatch.Success) {
      if (currentLine.Length + wordMatch.Value.Length > maxLength) {
        lines.Add(currentLine);
        currentLine = wordMatch.Value.TrimStart();
      } else {
        currentLine += wordMatch.Value;
      }
      wordMatch = wordMatch.NextMatch();
    }
    if (currentLine.Length > 0) {
      lines.Add(currentLine);
    }
    return lines;
  }

  /// <summary>Formats a config node key/value line.</summary>
  /// <param name="indentation">The indentation in tabs. Each tab is 8 spaces.</param>
  /// <param name="key">The key string.</param>
  /// <param name="value">
  /// The value string. It can contain multiple lines separated by a "\n" symbols.
  /// </param>
  /// <param name="meta">
  /// The meta block of this field. Only the applicable properties will be used. If set to <c>null</c>, then no
  /// comments/MM logic be dispatched.
  /// </param>
  /// <returns>A properly formatted line.</returns>
  static string MakeConfigNodeLine(int indentation, string key, string value, MetaBlock meta = null) {
    meta ??= new MetaBlock();
    var fieldName = (meta.mmCommand ?? "") + key + (meta.mmArguments ?? "");
    var operand = (meta.mmOperator ?? "") + "=";
    var fieldValue = EscapeValue(value);
    var comment = (meta.inlineComment != null ? " // " + meta.inlineComment : "");
    return new string('\t', indentation) + fieldName + " " + operand + " " + fieldValue + comment;
  }

  /// <summary>Escapes special symbols so that they don't break the formatting.</summary>
  /// <param name="value">The value to escape.</param>
  /// <returns>The escaped value.</returns>
  static string EscapeValue(string value) {
    // Turn the leading and the trailing spaces into the unicode codes. Otherwise, they won't load.
    if (value.Length > 0) {
      value = EscapeChar(value[0]) + value.Substring(1);
    }
    if (value.Length > 1) {
      value = value.Substring(0, value.Length - 1) + EscapeChar(value[value.Length - 1]);
    }
    // Also, escape the linefeed character since it breaks the formatting.
    return value.Replace("\n", "\\n").Replace("\t", "\\t");
  }

  /// <summary>Escapes a whitespace character.</summary>
  /// <param name="c">The character to escape.</param>
  /// <returns>The unicode encode (\uXXXX) character string, or the character itself.</returns>
  static string EscapeChar(char c) {
    return c == ' ' || c == '\u00a0' || c == '\t'
        ? "\\u" + ((int)c).ToString("x4")
        : "" + c;
  }
  #endregion
}

}  // namespace
