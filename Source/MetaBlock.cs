// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using System.Collections.Generic;
using KSPDev.LogUtils;

// ReSharper disable once CheckNamespace
namespace KSPDev.LocalizationTool {

/// <summary>Class that wraps various states regarding the non-logic significant things in the config files.</summary>
/// <remarks>
/// This class lets capturing aspects of the original config file formatting to allow restoring it as close as possible.
/// However, there is no intention to preserve the original formatting at any cost. There are some assumptions.  
/// </remarks>
public class MetaBlock {
  #region Public properties
  /// <summary>A line that is present in the original config file.</summary>
  public struct TrailingLine {
    /// <summary>Tells if the line was empty or consisting entirely of the whitespaces.</summary>
    public bool isEmptyLine;

    /// <summary>Gives a COMMENT in the line.</summary>
    /// <remarks>
    /// The value won't have the indentation and commenting symbols. It's only the actual commenting text. It can be
    /// empty, though.
    /// </remarks>
    public string value;
  }

  /// <summary>The lines that must precede the field or node.</summary>
  /// <remarks>
  /// Check the type of the line! Keep in mind that the values are <i>trimmed</i>. I.e. you should expect that the
  /// leading and trailing whitespaces were removed.
  /// </remarks>
  /// <value>The comments or empty lines that go <i>before</i> the entity.</value>
  public readonly List<TrailingLine> trailingLines = new();

  /// <summary>The comment that follows exactly after the key/value assignment.</summary>
  /// <remarks>
  /// This is the actual comment text, trimmed at the both sides. The leading commenting escape sequence (<c>//</c>) is
  /// not included.
  /// </remarks>
  /// <value>
  /// The comment that follow on the same line after a <c>//</c> escape sequence or <c>null</c> if nothing set.
  /// </value>
  public string inlineComment { get; private set; }

  /// <summary>The comment that follows immediately after the opening brace of a node.</summary>
  /// <value>The comment after the opening bracket symbol or <c>null</c> if nothing set.</value>
  public string openBlockComment { get; private set; }

  /// <summary>The comment that follows immediately after the closing brace of a node.</summary>
  /// <value>The comment after the closing bracket symbol or <c>null</c> if nothing set.</value>
  public string closeBlockComment { get; private set; }

  /// <summary>The Module Manager command.</summary>
  /// <remarks>It's a prefix symbol to the field name aor a block starter (e.g. <c>PART</c>).</remarks>
  /// <value>The MM command symbol like <c>@</c> or <c>%</c> or <c>null</c> if nothing set.</value>
  public string mmCommand { get; private set; }

  /// <summary>The Module Manager command arguments.</summary>
  /// <remarks>
  /// See MM Wiki for more details. In nutshell, it's everything that goes after the actual field name or the block
  /// starter term.
  /// </remarks>
  /// <value>The MM command arguments like <c>:NEEDS[KIS]</c> or <c>,*</c> or <c>null</c> if nothing set.</value>
  public string mmArguments { get; private set; }

  /// <summary>The Module Manager assignment operator.</summary>
  /// <remarks>
  /// It's a prefix symbol to the equals sign in the key/value declaration. E.g. if it was <c>+=</c> in the CFG, then it
  /// will be <c>+</c> here.
  /// </remarks>
  /// <value>The MM operator like <c>+</c>, <c>*</c>, <c>!</c> or <c>null</c> if nothing set.</value>
  public string mmOperator { get; private set; }

  /// <summary>Tells if the actual field must not be actually emitted due it's artificial.</summary>
  /// <remarks>
  /// The meta block properties that attribute the field itself (as the inline comment) make no sense in this mode.
  /// </remarks>
  /// <value><c>true</c> if the field must not be actually written into the output.</value>
  public bool isFakeField { get; private set; }
  #endregion

  #region Serialization tags
  const string MetaTrailingEmptyLine = "TrailingEmptyLine";
  const string MetaTrailingLinePrefix = "TrailingComment:";
  const string MetaInlineCommentPrefix = "InlineComment:";
  const string MetaOpenBlockCommentPrefix = "OpenBlockComment:";
  const string MetaCloseBlockCommentPrefix = "CloseBlockComment:";
  const string MetaIsFakeField = "IsFakeField";
  const string MetaModuleMangerCmdPrefix = "MMCmdComment:";
  const string MetaModuleMangerArgsPrefix = "MMArgsComment:";
  const string MetaModuleMangerOpPrefix = "MMOpComment:";
  #endregion

  #region API
  /// <summary>Tells if there is ANY usable info captured by this block.</summary>
  /// <returns>Returns <c>false</c> if it's OK to just drop this instance and disregard its state.</returns>
  public bool IsEmpty() {
    return trailingLines.Count == 0 && inlineComment == null && openBlockComment == null
        && closeBlockComment == null && mmCommand == null && mmOperator == null;
  }

  /// <summary>Adds a comment that precedes the element.</summary>
  /// <remarks>The original formatting of the comment will be lost!</remarks>
  /// <param name="newComment">The comment content. It will be trimmed and all the line feeds replaced by a space.</param>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock AddLineComment(string newComment) {
    if (newComment != null) {
      trailingLines.Add(new TrailingLine() { value=newComment.Trim().Replace("\n", " ") });
    }
    return this;
  }

  /// <summary>Indicates that a clear line feed must be emitted BEFORE the element.</summary>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock AddLineCommentBreak() {
    trailingLines.Add(new TrailingLine() { isEmptyLine=true });
    return this;
  }

  /// <summary>Sets a comment that is to be following the key/value assignment on the very same line.</summary>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetInlineComment(string newComment) {
    if (!string.IsNullOrEmpty(newComment)) {
      inlineComment = newComment;
    }
    return this;
  }

  /// <summary>Sets a comment that attributes the open bracket statement.</summary>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetOpenBlockComment(string newComment) {
    if (!string.IsNullOrEmpty(newComment)) {
      openBlockComment = newComment;
    }
    return this;
  }

  /// <summary>Sets a comment that attributes the close bracket statement.</summary>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetCloseBlockComment(string newComment) {
    if (!string.IsNullOrEmpty(newComment)) {
      closeBlockComment = newComment;
    }
    return this;
  }

  /// <summary>Sets the fake field state.</summary>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetIsFakeField(bool state) {
    isFakeField = state;
    return this;
  }

  /// <summary>Sets an MM patch command.</summary>
  /// <remarks>
  /// The <i>command</i> is a prefix to the field or a block starter. E.g. for "@PART" the command would be "@".
  /// </remarks>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetModuleManagerCommand(string newMmCommand) {
    if (!string.IsNullOrEmpty(newMmCommand)) {
      mmCommand = newMmCommand;
    }
    return this;
  }

  /// <summary>Sets an MM patch command arguments.</summary>
  /// <remarks>
  /// The <i>arguments</i> is what follows the field name or the node block opener. E.g. for "PART:NEEDS[KIS]" it would
  /// be ":NEEDS[KIS]".
  /// </remarks>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetModuleManagerArguments(string newMmArguments) {
    if (!string.IsNullOrEmpty(newMmArguments)) {
      mmArguments = newMmArguments;
    }
    return this;
  }

  /// <summary>Sets an MM patch operator.</summary>
  /// <remarks>
  /// The <i>operator</i> is a prefix to the equal sign in the key/value declaration. E.g. for "field += 20" the
  /// operator would be "+".
  /// </remarks>
  /// <returns>The <see cref="MetaBlock"/> instance. It lets chaining the setters.</returns>
  public MetaBlock SetModuleManagerOperator(string newMmOperator) {
    if (!string.IsNullOrEmpty(newMmOperator)) {
      mmOperator = newMmOperator;
    }
    return this;
  }

  /// <summary>Makes a block form the serialized string.</summary>
  /// <param name="value">The string value made via <see cref="SerializeToString"/>.</param>
  /// <returns>A new block that is initialized from the string.</returns>
  public static MetaBlock MakeFromString(string value) {
    var meta = new MetaBlock();
    meta.ParseFromString(value);
    return meta;
  }

  /// <summary>Captures the current state of the block and resets the instance.</summary>
  /// <returns>The serialized state of the block.</returns>
  /// <seealso cref="Reset"/>
  /// <seealso cref="ToString"/>
  public string FlushToString() {
    var res = SerializeToString();
    Reset();
    return res;
  }

  /// <inheritdoc/>
  public override string ToString() {
    return SerializeToString();
  }  
  #endregion

  #region Local utility methods
  /// <summary>Resets the block to it's default state (as on construction).</summary>
  void Reset() {
    trailingLines.Clear();
    inlineComment = null;
    openBlockComment = null;
    closeBlockComment = null;
    isFakeField = false;
    mmCommand = null;
    mmOperator = null;
  }

  /// <summary>Restores the block state form a serialized string.</summary>
  void ParseFromString(string input) {
    Reset();
    if (input == null) {
      return;
    }
    var lines = input.Split('\n');
    foreach (var line in lines) {
      if (line == MetaTrailingEmptyLine) {
        trailingLines.Add(new TrailingLine() {isEmptyLine = true});
      } else if (line.StartsWith(MetaTrailingLinePrefix)) {
        trailingLines.Add(
            new TrailingLine() {
                value = line.Substring(MetaTrailingLinePrefix.Length)
            });
      } else if (line.StartsWith(MetaInlineCommentPrefix)) {
        inlineComment = line.Substring(MetaInlineCommentPrefix.Length);
      } else if (line.StartsWith(MetaOpenBlockCommentPrefix)) {
        openBlockComment = line.Substring(MetaOpenBlockCommentPrefix.Length);
      } else if (line.StartsWith(MetaCloseBlockCommentPrefix)) {
        closeBlockComment = line.Substring(MetaCloseBlockCommentPrefix.Length);
      } else if (line.StartsWith(MetaModuleMangerCmdPrefix)) {
        mmCommand = line.Substring(MetaModuleMangerCmdPrefix.Length);
      } else if (line.StartsWith(MetaModuleMangerArgsPrefix)) {
        mmArguments = line.Substring(MetaModuleMangerArgsPrefix.Length);
      } else if (line.StartsWith(MetaModuleMangerOpPrefix)) {
        mmOperator = line.Substring(MetaModuleMangerOpPrefix.Length);
      } else if (line == MetaIsFakeField) {
        isFakeField = true;
      } else {
        DebugEx.Error("Cannot parse MetaBlock: {0}", line);
      }
    }
  }

  /// <summary>Saves the block state as a string.</summary>
  string SerializeToString() {
    var res = new List<string>();
    if (trailingLines.Count > 0) {
      foreach (var trailingLine in trailingLines) {
        if (trailingLine.isEmptyLine) {
          res.Add(MetaTrailingEmptyLine);
        } else {
          res.Add(MetaTrailingLinePrefix + trailingLine.value);
        }
      }
    }
    if (inlineComment != null) {
      res.Add(MetaInlineCommentPrefix + inlineComment);
    }
    if (openBlockComment != null) {
      res.Add(MetaOpenBlockCommentPrefix + openBlockComment);
    }
    if (closeBlockComment != null) {
      res.Add(MetaCloseBlockCommentPrefix + closeBlockComment);
    }
    if (isFakeField) {
      res.Add(MetaIsFakeField);
    }
    if (mmCommand != null) {
      res.Add(MetaModuleMangerCmdPrefix + mmCommand);
    }
    if (mmArguments != null) {
      res.Add(MetaModuleMangerArgsPrefix + mmArguments);
    }
    if (mmOperator != null) {
      res.Add(MetaModuleMangerOpPrefix + mmOperator);
    }
    return res.Count > 0 ? string.Join("\n", res) : null;
  }
  #endregion
}

}  // namespace
