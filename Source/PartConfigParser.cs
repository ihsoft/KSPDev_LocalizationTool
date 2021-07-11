// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSP.Localization;
using KSPDev.LogUtils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// ReSharper disable once CheckNamespace
namespace KSPDev.LocalizationTool {

/// <summary>Parser for the part configs with comments and localization support.</summary>
/// <remarks>
/// <para>
/// This parser does <i>not</i> use the stock code to parse a config file. This may result in
/// differences on how the game and this parser see the config. Don't use this parser unless
/// accessing to the comments and flexible control of the localization is required.
/// </para>
/// <para>
/// The stock class can only save the comments to the config file, but not loading them back. This
/// helper class is a custom parser that can parse a simple config file while keeping the comments.
/// It also gives better controls on how to handle the localized values.
/// </para>
/// </remarks>
public sealed class PartConfigParser {
  readonly bool _localizeValues;

  /// <summary>Parses a beginning of the multiline subnode declaration.</summary>
  /// <remarks>
  /// It detects the staring of the block and returns the key (subnode name) as <c>$2</c>.
  /// </remarks>
  /// <example>
  /// <code><![CDATA[
  /// MODULE
  /// {
  ///   foo = bar
  /// }
  /// ]]></code>
  /// </example>
  static readonly Regex NodeMultiLinePrefixDeclRe = new(@"^\s*(\W?)(\S+)\s*(.*?)\s*$");

  /// <summary>Parses a beginning subnode declaration that starts on the same line.</summary>
  /// <remarks>
  /// It detects the staring of the block and returns the key (subnode name) as <c>$2</c> and
  /// everything after the opening bracket as <c>$3</c>.
  /// </remarks>
  /// <example>
  /// <code><![CDATA[
  /// MODULE { foo = bar }
  /// foo {} bar {}
  /// ]]></code>
  /// </example>
  static readonly Regex NodeSameLineDeclRe = new(@"^\s*(\W?)(\S*)\s*{\s*(.*?)\s*$");

  /// <summary>Parses a simple key/value pair.</summary>
  /// <remarks>
  /// The any bracket symbol work as a stop symbol! The key is returned as <c>$2</c> and the value is returned as
  /// <c>$3</c>.
  /// </remarks>
  /// <example>
  /// <code><![CDATA[
  /// foo = bar
  /// ]]></code>
  /// </example>
  static readonly Regex KeyValueLineDeclRe = new(@"^\s*(\W?)(\S+)\s*(\W?)=\s*(.*?)\s*(//\s*(.*))?$");

  static readonly Regex MmKeyDeleteCommandDeclRe = new(@"^\s*([\-!]{1})(\S+)\s*(//.*)?$");

  /// <summary>Parses a comment that takes the whole line.</summary>
  static readonly Regex CommentDeclRe = new(@"^\s*//\s*(.*?)\s*?$");
  
  /// <summary>Creates a parser with the desired options.</summary>
  /// <param name="localizeValues">
  /// Tells if the field values should be localized. If not, then they are returned from the file
  /// "as-is".
  /// </param>
  /// <returns>A loaded config node.</returns>
  public PartConfigParser(bool localizeValues = true) {
    _localizeValues = localizeValues;
  }

  /// <summary>Parses the file as a stock <c>ConfigNode</c>.</summary>
  /// <remarks>Note, that the stock <c>ConfigNode</c> class cannot properly handle the comments,
  /// placed in between the subnodes declaration.
  /// </remarks>
  /// <param name="fileFullName">The path to the file to parse.</param>
  /// <returns>The config node or <c>null</c> if error happen.</returns>
  public ConfigNode ParseFileAsNode(string fileFullName) {
    if (!File.Exists(fileFullName)) {
      DebugEx.Error("Cannot find CFG file: {0}", fileFullName);
      return null;
    }

    var lines = File.ReadAllLines(fileFullName)
        .Select(x => x.Trim())
        .ToList();
    var nodesStack = new List<ConfigNode>() { new ConfigNode() };
    var node = nodesStack[0];
    var lineNum = 0;
    var meta = new MetaBlock();
    while (lineNum < lines.Count) {
      var line = lines[lineNum];

      // Check for the node section close.
      if (line.StartsWith("}", StringComparison.Ordinal)) {
        // Flush the unclaimed block comments as a comment field
        if (!meta.IsEmpty()) {
          node.AddValue("__fakeField", "", meta.SetIsFakeField(true).FlushToString());
        }

        nodesStack.RemoveAt(nodesStack.Count - 1);
        if (nodesStack.Count == 0) {
          ReportParseError(fileFullName, line, lineNum, message: "Unexpected node close statement");
          return null;
        }
        var ownerNode = node;
        node = nodesStack[nodesStack.Count - 1];

        line = line.Substring(1).TrimStart();  // Chop-off "}".
        if (line.Length == 0) {
          lineNum++;
          continue;
        }

        // Check if it's a closing bracket comment.
        var commentMatch = CommentDeclRe.Match(line);
        if (commentMatch.Success) {
          ownerNode.comment = MetaBlock.MakeFromString(ownerNode.comment)
              .SetCloseBlockComment(commentMatch.Groups[1].Value)
              .ToString();
          lineNum++;
        } else {
          lines[lineNum] = line;  // There's something left in the line, re-try it.
        }
        continue;
      }

      // CASE #1: Empty line.
      if (line.Length == 0) {
        // Accumulate it as an empty line in the block comment.
        meta.AddLineCommentBreak();
        lineNum++;
        continue;
      }

      // CASE #2: Line comment.
      var lineMatch = CommentDeclRe.Match(line);
      if (lineMatch.Success) {
        // Accumulate in the whole line block comment.
        meta.AddLineComment(lineMatch.Groups[1].Value);
        lineNum++;
        continue;
      }

      // CASE #3: Key value pair.
      lineMatch = KeyValueLineDeclRe.Match(line);
      if (lineMatch.Success) {
        // May have an in-line existingComment, make it a property of the value.
        // Field name may have an MM command prefix.
        // Assignment statement may have an MM operator prefix.
        var fieldName = lineMatch.Groups[2].Value;
        var fieldValue = lineMatch.Groups[4].Value;
        var commentValue = lineMatch.Groups[6].Value;

        // Localize the value if it starts from "#". There can be false positives.
        if (_localizeValues && LocalizationManager.IsLocalizationTag(fieldValue)) {
          var locValue = Localizer.Format(fieldValue);
          if (!LocalizationManager.IsLocalizationTag(commentValue, firstWordOnly: true)) {
            // Simulate the localized existingComment if one is missing. It will be used when updating parts.
            commentValue = fieldValue + " = " + locValue;
          }
          fieldValue = locValue;
        }
        meta.SetModuleManagerCommand(lineMatch.Groups[1].Value)
            .SetModuleManagerOperator(lineMatch.Groups[3].Value)
            .SetInlineComment(commentValue);
        node.AddValue(fieldName, fieldValue, meta.FlushToString());
        lineNum++;
        continue;
      }

      // CASE #5: Node, that starts on the same line.
      lineMatch = NodeSameLineDeclRe.Match(line);
      if (lineMatch.Success) {
        // The node declaration starts on the same line. There can be more data in the same line!
        // Everything, which is not a existingComment, is processed as a nex line.
        // The same line existingComment is get assigned to the node.
        var moduleManagerCmd = lineMatch.Groups[1].Value;
        var nodeName = lineMatch.Groups[2].Value;
        var lineLeftOff = lineMatch.Groups[3].Value;
        meta.SetModuleManagerCommand(moduleManagerCmd);
        node = node.AddNode(nodeName, meta.FlushToString());
        nodesStack.Add(node);
        if (lineLeftOff.Length > 0) {
          lines[lineNum] = lineLeftOff;
        } else {
          lineNum++;
        }
        continue;
      }

      // CASE #6: Node, that starts on the next line(s).
      lineMatch = NodeMultiLinePrefixDeclRe.Match(line);
      if (lineMatch.Success) {
        var moduleManagerCmd= lineMatch.Groups[1].Value;//FIXME
        var nodeName = lineMatch.Groups[2].Value;
        var lineLeftOff = lineMatch.Groups[3].Value;
        if (lineLeftOff != "") {
          var commentValue = ExtractComment(lineLeftOff);
          if (commentValue == null) {
            ReportParseError(fileFullName, line, lineNum);
            return null;
          }
          meta.SetInlineComment(commentValue);
        }
        meta.SetModuleManagerCommand(moduleManagerCmd);
        var startLine = lineNum ;
        lineNum++;

        // Find the opening bracket in the following lines, capturing the possible comments.
        for (; lineNum < lines.Count; lineNum++) {
          var skipLine = lines[lineNum];
          if (skipLine.Length == 0) {
            // Empty line before the opening bracket  cannot be preserved.
            DebugEx.Warning(
                "Ignoring empty line before opening bracket: file={0}, line={1}", fileFullName, lineNum + 1);
            continue;
          }
          var commentMatch = CommentDeclRe.Match(skipLine);
          if (commentMatch.Success) {
            // A comment before the opening bracket cannot be preserved.
            DebugEx.Warning(
                "Ignoring a comment before opening bracket: file={0}, line={1}", fileFullName, lineNum + 1);
            continue;
          }
          break;  // The open bracket line candidate found.
        }
        if (lineNum >= lines.Count) {
          DebugEx.Warning(
              "Skipping a bad multiline node: file={0}, fieldName={1}, lines={2}-{3}. End of file reached",
              fileFullName, nodeName, startLine + 1, lineNum + 1);
          continue;
        }
        var bracketLine = lines[lineNum];
        if (!bracketLine.StartsWith("{", StringComparison.Ordinal)) {
          // Module Manager delete node/value commands are allowed to not have the opening bracket.
          if (moduleManagerCmd == "!" || moduleManagerCmd == "-") {
            node = node.AddNode(nodeName, meta.FlushToString());
            nodesStack.Add(node);
            continue;
          }
          DebugEx.Warning("Skipping field/node without value: file={0}, fieldName={1}, line={2}",
                          fileFullName, nodeName, startLine);
          continue;
        }

        // Unwrap the data after the opening bracket.
        lineLeftOff = bracketLine.Substring(1).Trim();  // Chop off "{"
        if (lineLeftOff.Length > 0) {
          var commentMatch = CommentDeclRe.Match(lineLeftOff);
          if (commentMatch.Success) {
            meta.SetOpenBlockComment(commentMatch.Groups[1].Value);
            lineNum++;  // This line is done.
          } else {
            // The left-off comment is a real data. Stay at the line, but resume parsing.
            lines[lineNum] = lineLeftOff;
          }
        } else {
          lineNum++;  // This line is done.
        }

        node = node.AddNode(nodeName, meta.FlushToString());
        nodesStack.Add(node);
        continue;
      }

      // NOT A CASE! We must never end up here.
      ReportParseError(fileFullName, line, lineNum);
      return null;
    }

    if (nodesStack.Count > 1) {
      DebugEx.Error("Cannot properly parse file: {0}. The content can be wrong.", fileFullName);
    }

    return nodesStack[0];
  }

  #region Local utility methods
  /// <summary>reports a verbose error to the logs.</summary>
  static void ReportParseError(string configFile, string lineContent, int lineNum,
                               string message = null) {
    if (string.IsNullOrEmpty(message)) {
      DebugEx.Error("Error parsing file {0}, line {1}. Cannot consume content:\n{2}",
                    configFile, lineNum, lineContent);
    } else {
      DebugEx.Error("Error parsing file {0}, line {1}. {2} in:\n{3}",
                    configFile, lineNum, message, lineContent);
    }
  }

  /// <summary>Extracts the actual comment body from the escaped string.</summary>
  static string ExtractComment(string rawString) {
    if (string.IsNullOrEmpty(rawString)) {
      return "";
    }
    var match = CommentDeclRe.Match(rawString);
    return match.Success ? match.Groups[1].Value : null;
  } 
  #endregion
}

}  // namespace
