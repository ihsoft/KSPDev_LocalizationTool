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

/// <summary>Parser for the part configs with comments and localization support.</summary>
/// <remarks>
/// <para>
/// This parser does <i>not</i> use the stock code to parse a config file. This may result in
/// differences on how the game and this parser see the config. Don't use this parser unless
/// accessing to the comments and flexible control of the localziation is required.
/// </para>
/// <para>
/// The stock class can only save the comments to the config file, but not loading them back. This
/// helper class is a custom parser that can parse a simple config file while keeping the comments.
/// It also gives better controls on how to handle the localized values.
/// </para>
/// </remarks>
public sealed class PartConfigParser {
  readonly bool localizeValues;
  readonly bool skipLineComments;

  /// <summary>Parses a beginning of the multiline subnode declration.</summary>
  /// <remarks>
  /// <para>
  /// It detects the staring of the block and returns the key (subnode name) as <c>$1</c>:
  /// </para>
  /// <code>
  /// MODULE
  /// {
  ///   foo = bar
  /// }
  /// </code>
  /// </remarks>
  readonly Regex NodeMultiLinePrefixDeclRe = new Regex(@"^\s*(\S+)\s*$");

  /// <summary>Parses a beginning subnode declration that starts on the same line.</summary>
  /// <remarks>
  /// <para>
  /// It detects the staring of the block and returns the key (subnode name) as <c>$1</c> and
  /// everything after the opening bracket as <c>$2</c>:
  /// </para>
  /// <code>
  /// // Multi-line.
  /// MODULE {
  ///   foo = bar
  /// }
  /// // Single line.
  /// MODULE { foo = bar }
  /// </code>
  /// </remarks>
  readonly Regex NodeSameLineDeclRe = new Regex(@"^\s*(\S+)\s*{\s*(.*)$");

  /// <summary>Parses a simple key/value pair.</summary>
  /// <remarks>
  /// <para>
  /// The closing bracket symbols works as a stop symbol! The key is returned as <c>$1</c> and
  /// the value is returned as <c>$2</c>:
  /// </para>
  /// <code>
  /// // Multi-line: one value.
  /// foo = bar
  /// // Single line: two nodes.
  /// foo = {} bar = {}
  /// </code>
  /// </remarks>
  readonly Regex KeyValueLineDeclRe = new Regex(@"^\s*(\S+)\s*=\s*(.*?)\s*(}.*)?$");

  /// <summary>Creates a parser with the desired options.</summary>
  /// <param name="localizeValues">
  /// Tells if the field values should be localized. If not, then they are returned from the file
  /// "as-is".
  /// </param>
  /// <param name="skipLineComments">
  /// Tells to not emit the "__commentField" fields for the standalone comments. It doesn't affect
  /// the in-line comments; they are always assigned to the related value or subnode.
  /// </param>
  /// <returns>A loaded config node.</returns>
  public PartConfigParser(bool localizeValues = true,
                          bool skipLineComments = false) {
    this.localizeValues = localizeValues;
    this.skipLineComments = skipLineComments;
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
    while (lineNum < lines.Count) {
      var line = lines[lineNum];
      if (line.Length == 0) {
        lineNum++;
        if (!skipLineComments) {
          node.AddValue("__commentField", "");
        }
        continue;
      }

      // Check for the node section close.
      if (line.StartsWith("}", StringComparison.Ordinal)) {
        nodesStack.RemoveAt(nodesStack.Count - 1);
        if (nodesStack.Count == 0) {
          ReportParseError(fileFullName, line, lineNum, message: "Unexpected node close statement");
          return null;
        }
        node = nodesStack[nodesStack.Count - 1];
        line = line.Substring(1).TrimStart();  // Chop-off "}".
        if (line.Length == 0) {
          lineNum++;
        } else {
          lines[lineNum] = line;
        }
        continue;
      }

      // Chop off the comment.
      string comment = null;
      var commentPos = line.IndexOf("//", StringComparison.Ordinal);
      if (commentPos != -1) {
        comment = line.Substring(commentPos + 2).TrimStart();
        line = line.Substring(0, commentPos).TrimEnd();
        if (line.Length == 0) {
          lines.RemoveAt(0);
          lineNum++;
          if (!skipLineComments) {
            node.AddValue("__commentField", comment);
          }
          continue;
        }
      }
      string lineLeftOff = null;

      // Try handling the simplest case: a key value pair (with an optional comment).
      var keyValueMatch = KeyValueLineDeclRe.Match(line);
      if (keyValueMatch.Success) {
        // Localize the value if it starts from "#". There can be false positives.
        var value = keyValueMatch.Groups[2].Value;
        if (localizeValues && value.StartsWith("#", StringComparison.Ordinal)) {
          var locValue = Localizer.Format(value);
          if (comment == null || !comment.StartsWith("#", StringComparison.Ordinal)) {
            // Simulate the localized comment if one is missing. It will be used when updating
            // parts.
            comment = value + " = " + locValue;
          }
          value = locValue;
        }
        node.AddValue(keyValueMatch.Groups[1].Value, value, comment);
        line = keyValueMatch.Groups[3].Value;
        if (string.IsNullOrEmpty(line)) {
          lineNum++;
        } else {
          lines[lineNum] = line;
        }
        continue;
      }

      // At this point we know it's a subnode.
      string nodeName = null;
      if (NodeSameLineDeclRe.IsMatch(line)) {
        // The node declaration starts on the same line. There can be more data in the same line!
        var sameLineMatch = NodeSameLineDeclRe.Match(line);
        nodeName = sameLineMatch.Groups[1].Value;
        lineLeftOff = sameLineMatch.Groups[2].Value;
      } else if (NodeMultiLinePrefixDeclRe.IsMatch(line)) {
        var firstNonEmpty = lines
            .Skip(lineNum + 1)
            .SkipWhile(l => l.Length == 0)
            .FirstOrDefault();
        if (firstNonEmpty != null && firstNonEmpty.StartsWith("{", StringComparison.Ordinal)) {
          var multiLineMatch = NodeMultiLinePrefixDeclRe.Match(line);
          nodeName = multiLineMatch.Groups[1].Value;
          lineNum++;
          while (lines[lineNum].Length == 0) {
            lineNum++;
          }
          lineLeftOff = lines[lineNum].Substring(1);  // Chop off "{"
        }
      }
      if (string.IsNullOrEmpty(lineLeftOff)) {
        lineNum++;
      } else {
        lines[lineNum] = lineLeftOff.TrimStart();
      }
      if (nodeName == null) {
        ReportParseError(fileFullName, line, lineNum);
        return null;
      }
      var newNode = node.AddNode(nodeName, comment);
      nodesStack.Add(newNode);
      node = newNode;
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
  #endregion
}
