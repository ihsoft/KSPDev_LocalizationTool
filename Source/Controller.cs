// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSP.Localization;
using KSPDev.ConfigUtils;
using KSPDev.FSUtils;
using KSPDev.GUIUtils;
using KSPDev.LogUtils;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSPDev.LocalizationTool {

[PersistentFieldsFileAttribute("KSPDev/LocalizationTool/PluginData/settings.cfg", "")]
[PersistentFieldsFileAttribute("KSPDev/LocalizationTool/PluginData/session.cfg", "UI",
                               StdPersistentGroups.SessionGroup)]
sealed class Controller : MonoBehaviour, IHasGUI {
  #region Localizable UI strings
  static readonly Message<Version> MainWindowTitleTxt = new Message<Version>(
      "#locTool_00000",
      "KSPDev LocalizationTool v<<1>>");

  static readonly Message MakeSelectionsForExportTxt = new Message(
      "#locTool_00001",
      "<i>EXPORT STRINGS: Select an assembly or a parts folder</i>");

  static readonly Message<int, int> ExportBtnTxt = new Message<int, int>(
      "#locTool_00002",
      "Export strings from <<1>> parts and <<2>> assemblies into exported.cfg");

  static readonly Message MakeSelectionsForReloadTxt = new Message(
      "#locTool_00003",
      "<i>RELOAD STRINGS: Select a localization file</i>");

  static readonly Message<int, int> RefreshBtnTxt = new Message<int, int>(
      "#locTool_00004",
      "Reload <<1>> localization configs and update <<2>> parts");

  static readonly Message UpdateAllPartsTxt = new Message(
      "#locTool_00005",
      "Update all the parts in the game DB");

  static readonly Message TypePrefixToStartTxt = new Message(
      "#locTool_00006",
      "<i>...type 3 or more prefix characters...</i>");

  static readonly Message NothingFoundForPrefixTxt = new Message(
      "#locTool_00007",
      "<i>...nothing found for the prefix...</i>");

  static readonly Message UrlPrefixFieldCaptionTxt = new Message(
      "#locTool_00008",
      "URL prefix:");

  static readonly Message AssembliesWithoutModulesToggleTxt = new Message(
      "#locTool_00009",
      "Show assemblies with no modules");

  static readonly Message MakeSelectionsForPatchTxt = new Message(
      "#locTool_00010",
      "<i>EXPORT PART CONFIGS: Select a parts folder</i>");

  static readonly Message<int> PatchPartsBtnTxt = new Message<int>(
      "#locTool_00011",
      "Export <<1>> patched part configs");

  static readonly Message CloseDialogBtnTxt = new Message(
      "#locTool_00012",
      "Close");

  static readonly Message StringsExportedDlgTitle = new Message(
      "#locTool_00013",
      "Strings exported");

  static readonly Message<string> FileSavedTxt = new Message<string>(
      "#locTool_00014",
      "File saved:\n<<1>>");

  static readonly Message ConfigSavedDlgTitle = new Message(
      "#locTool_00015",
      "Part configs saved");

  static readonly Message<int, string> ConfigsSavedInFolderTxt = new Message<int, string>(
      "#locTool_00016",
      "<<1>> part configs saved into folder:\n<<2>>");

  static readonly Message WaitDialogTitle = new Message(
      "#locTool_00017",
      "Action in progress");

  static readonly Message WaitDialogText = new Message(
      "#locTool_00018",
      "It may take a while. Please, be patient...");

  static readonly Message CurrentLanguageFieldCaptionTxt = new Message(
      "#locTool_00019",
      "Current language:");

  static readonly Message<string, int> PartsRecordTxt = new Message<string, int>(
      "#locTool_00020",
      "<<1>> (<<2>> parts)");
  
  static readonly Message<string, string, int> AssemblyRecordTxt = new Message<string, string, int>(
      "#locTool_00021",
      "<<1>>, v<<2>> (<<3>> modules)");

  static readonly Message<string, string, int> ConfigRecordTxt = new Message<string, string, int>(
      "#locTool_00022",
      "<<1>>, lang=<<2>> (<<3>> strings)");
  #endregion

  #region GUI scrollbox records
  /// <summary>Base class for the records that represent the extractor entities.</summary>
  abstract class ScannedRecord {
    public bool selected;
    public virtual void GuiAddItem() {
      selected = GUILayout.Toggle(selected, ToString());
    }
  }

  /// <summary>Simple item to display info in the scroll box. It cannot be selected.</summary>
  class StubRecord : ScannedRecord {
    public string stubText;
    public override void GuiAddItem() {
      GUILayout.Label(stubText);
    }
  }

  /// <summary>Item that represents mod parts record.</summary>
  class PartsRecord : ScannedRecord {
    public List<AvailablePart> parts = new List<AvailablePart>();
    public string urlPrefix = "";

    /// <inheritdoc/>
    public override string ToString() {
      return PartsRecordTxt.Format(urlPrefix, parts.Count);
    }
  }

  /// <summary>Item that represents an assembly record.</summary>
  class AssemblyRecord : ScannedRecord {
    public Assembly assembly;
    public List<Type> types;

    /// <inheritdoc/>
    public override string ToString() {
      return AssemblyRecordTxt.Format(
          KspPaths.MakeRelativePathToGameData(assembly.Location),
          assembly.GetName().Version.ToString(), types.Count);
    }
  }

  /// <summary>Item that represents a localization config.</summary>
  class ConfigRecord : ScannedRecord {
    public string url;
    public string filePath;
    public string lang;
    public ConfigNode node;

    /// <inheritdoc/>
    public override string ToString() {
      return ConfigRecordTxt.Format(
          KspPaths.MakeRelativePathToGameData(url), lang, node.GetValues().Length);
    }
  }
  #endregion

  #region Mod's settings
  [PersistentField("UI/toggleConsoleKey")]
  string _toggleConsoleKey = "&f8";

  [PersistentField("UI/scrollHeight")]
  // ReSharper disable once FieldCanBeMadeReadOnly.Local
  // ReSharper disable once ConvertToConstant.Local
  static int _scrollHeight = 150;

  /// <summary>Order, in which the part sections should be sorted.</summary>
  [PersistentField("Export/partFieldsSorting")]
  // ReSharper disable once FieldCanBeMadeReadOnly.Global
  // ReSharper disable once ConvertToConstant.Global
  internal static string partFieldsSorting = "title,manufacturer,description,tags";

  /// <summary>Tag prefixes that should be completely ignored by the tool.</summary>
  [PersistentField("Export/skipTags", isCollection = true)]
  // ReSharper disable once CollectionNeverUpdated.Global
  // ReSharper disable once FieldCanBeMadeReadOnly.Global
  internal static HashSet<string> skipTags = new HashSet<string>();

  /// <summary>Tag prefixes that are allowed for multiple usage.</summary>
  /// <remarks>They will be added as a separate group at the end of the lang file.</remarks>
  [PersistentField("Export/globalTags", isCollection = true)]
  // ReSharper disable once CollectionNeverUpdated.Global
  // ReSharper disable once FieldCanBeMadeReadOnly.Global
  internal static HashSet<string> globalPrefix = new HashSet<string>();
  #endregion

  #region Session settings
  //FIXME: handle resolution change!
  [PersistentField("windowPos", group = StdPersistentGroups.SessionGroup)]
  static Vector2 _windowPos = new Vector2(0, 0);

  /// <summary>Specifies if debug console is visible.</summary>
  [PersistentField("isOpen", group = StdPersistentGroups.SessionGroup)]
  static bool _isUiVisible;

  [PersistentField("lookupPrefix", group = StdPersistentGroups.SessionGroup)]
  string _lookupPrefix = "";

  [PersistentField("showNoModulesAssemblies", group = StdPersistentGroups.SessionGroup)]
  bool _allowNoModulesAssemblies;
  #endregion

  /// <summary>A list of actions to apply at the end of the GUI frame.</summary>
  // ReSharper disable once InconsistentNaming
  static readonly GuiActionsList _guiActions = new GuiActionsList();

  #region Local fields
  // ReSharper disable once InconsistentNaming
  static readonly Vector2 _windowSize = new Vector2(430, 0);
  static Rect _windowRect;
  List<ScannedRecord> _targets;
  Vector2 _partsScrollPos;
  string _lastCachedLookupPrefix;
  Event _toggleConsoleKeyEvent;
  const string ModalDialogId = "LocToolModalDialog";
  string _selectedLanguage;
  HermeticGUIControlText _selectedLanguageControl;

  GuiScale _guiScale;
  GUIStyle _guiNoWrapLabel;
  #endregion

  #region Default locale
  int _defLocaleVersion = -1;
  Dictionary<string, string> defaultLocaleLookup {
    get {
      if (_defLocaleVersion != LocalizableMessage.systemLocVersion) {
        _defLocaleVersion = LocalizableMessage.systemLocVersion;
        _defaultLocaleLookup = GameDatabase.Instance.GetConfigs("Localization")
            .SelectMany(n => n.config.nodes.Cast<ConfigNode>())
            .Where(x => x.name == "en-us")
            .SelectMany(v => v.values.Cast<ConfigNode.Value>())
            .ToDictionary(r => r.name, r => r.value);
        DebugEx.Warning(
            "Default locale strings reloaded: {0} entries updated.", defaultLocaleLookup.Count);
      }
      return _defaultLocaleLookup;
    }
  }
  Dictionary<string, string> _defaultLocaleLookup;
  #endregion

  #region MonoBehaviour overrides 
  /// <summary>Only loads session settings.</summary>
  void Awake() {
    ConfigAccessor.ReadFieldsInType(typeof(Controller), null /* instance */);
    ConfigAccessor.ReadFieldsInType(typeof(Controller), this, group: StdPersistentGroups.SessionGroup);

    _toggleConsoleKeyEvent = Event.KeyboardEvent(_toggleConsoleKey);
    _windowRect = new Rect(_windowPos, _windowSize);

    var langField = typeof(Controller).GetField(
        nameof(_selectedLanguage), BindingFlags.NonPublic | BindingFlags.Instance);
    _selectedLanguageControl = new HermeticGUIControlText(
        this, langField, useOwnLayout: true,
        onAfterUpdate: () => StartCoroutine(ExecuteLongAction(GuiActionSetLanguage)));
    _selectedLanguage = Localizer.CurrentLanguage;
    _guiScale = new GuiScale(
        getPivotFn: () => new Vector2(_windowRect.x, _windowRect.y), onScaleUpdatedFn: MakeGuiStyles);
  }

  /// <summary>Only stores session settings.</summary>
  void OnDestroy() {
    _windowPos = _windowRect.position;
    ConfigAccessor.WriteFieldsFromType(
        typeof(Controller), this, group: StdPersistentGroups.SessionGroup);
  }
  #endregion

  #region IHasGUI implementation
  /// <inheritdoc/>
  public void OnGUI() {
    if (Event.current.Equals(_toggleConsoleKeyEvent)) {
      Event.current.Use();
      _isUiVisible = !_isUiVisible;
      _targets = null;
    }
    if (_isUiVisible) {
      if (_targets == null) {
        GuiActionUpdateTargets(_lookupPrefix);
      }
      using (new GuiMatrixScope()) {
        _guiScale.UpdateMatrix();
        _windowRect = GUILayout.Window(
            GetInstanceID(), _windowRect, MakeConsoleWindow,
            MainWindowTitleTxt.Format(GetType().Assembly.GetName().Version));
      }
    }
  }
  #endregion

  /// <summary>Shows a UI dialog.</summary>
  /// <param name="windowId">The window ID. Unused.</param>
  void MakeConsoleWindow(int windowId) {
    _guiActions.ExecutePendingGuiActions();
    // Search prefix controls.
    using (new GUILayout.HorizontalScope(GUI.skin.box)) {
      GUILayout.Label(UrlPrefixFieldCaptionTxt, _guiNoWrapLabel, GUILayout.ExpandWidth(false));
      _lookupPrefix = GUILayout.TextField(_lookupPrefix, GUILayout.ExpandWidth(true)).TrimStart();
      if (_lookupPrefix != _lastCachedLookupPrefix) {
        _lastCachedLookupPrefix = _lookupPrefix;
        _guiActions.Add(() => GuiActionUpdateTargets(_lookupPrefix));
      }
    }

    // Found items scroll view.
    using (var scrollScope = new GUILayout.ScrollViewScope(
        _partsScrollPos, GUI.skin.box, GUILayout.Height(_scrollHeight))) {
      _partsScrollPos = scrollScope.scrollPosition;
      foreach (var target in _targets) {
        target.GuiAddItem();
      }
    }

    GUI.changed = false;
    _allowNoModulesAssemblies =
        GUILayout.Toggle(_allowNoModulesAssemblies, AssembliesWithoutModulesToggleTxt);
    if (GUI.changed) {
      _guiActions.Add(() => GuiActionUpdateTargets(_lookupPrefix));
    }

    // Action buttons.
    var selectedModulesCount = _targets.OfType<AssemblyRecord>()
        .Where(x => x.selected)
        .Sum(x => x.types.Count);
    var selectedPartsCount = _targets.OfType<PartsRecord>()
        .Where(x => x.selected)
        .Sum(x => x.parts.Count);
    var selectedLacsCount = _targets.OfType<ConfigRecord>()
        .Count(x => x.selected);

    var selectedAssemblies = _targets.OfType<AssemblyRecord>().Where(x => x.selected).ToArray();
    var selectedParts = _targets.OfType<PartsRecord>().Where(x => x.selected).ToArray();
    var selectedConfigs = _targets.OfType<ConfigRecord>().Where(x => x.selected).ToArray();

    // Strings export controls.
    if (selectedPartsCount > 0
        || _allowNoModulesAssemblies && selectedAssemblies.Any()
        || !_allowNoModulesAssemblies && selectedModulesCount > 0) {
      var title = ExportBtnTxt.Format(selectedParts.Sum(x => x.parts.Count),
                                      selectedAssemblies.Length);
      if (GUILayout.Button(title)) {
        GuiActionExportStrings(selectedParts, selectedAssemblies);
      }
    } else {
      GUI.enabled = false;
      GUILayout.Button(MakeSelectionsForExportTxt);
      GUI.enabled = true;
    }

    // Parts export controls.
    if (selectedPartsCount > 0) {
      var title = PatchPartsBtnTxt.Format(selectedParts.Sum(x => x.parts.Count));
      if (GUILayout.Button(title)) {
        GuiExportPartConfigs(selectedParts);
      }
    } else {
      GUI.enabled = false;
      GUILayout.Button(MakeSelectionsForPatchTxt);
      GUI.enabled = true;
    }

    // Strings reload controls.
    if (selectedLacsCount > 0) {
      var title = RefreshBtnTxt.Format(
          selectedConfigs.Length, selectedParts.Sum(x => x.parts.Count));
      if (GUILayout.Button(title)) {
        StartCoroutine(ExecuteLongAction(
            () => GuiActionRefreshStrings(selectedConfigs, selectedParts)));
      }
    } else {
      GUI.enabled = false;
      GUILayout.Button(MakeSelectionsForReloadTxt);
      GUI.enabled = true;
    }

    // Parts DB update controls.
    if (GUILayout.Button(UpdateAllPartsTxt)) {
      StartCoroutine(ExecuteLongAction(GuiActionUpdateAllParts));
    }

    using (new GUILayout.HorizontalScope(GUI.skin.box)) {
      GUILayout.Label(CurrentLanguageFieldCaptionTxt, _guiNoWrapLabel);
      GUILayout.FlexibleSpace();
      _selectedLanguageControl.RenderControl(
          _guiActions, GUIStyle.none, new[] {GUILayout.Width(100)});
    }

    GUI.DragWindow();
  }

  /// <summary>Makes the styles when scale is changed or initiated.</summary>
  void MakeGuiStyles() {
    _guiNoWrapLabel = new GUIStyle(GUI.skin.label) {
        wordWrap = false,
    };
  }

  /// <summary>Saves the strings for the selected entities into a new file.</summary>
  /// <param name="parts">The parts to export the strings from.</param>
  /// <param name="assemblies">The mod assemblies to export the strings from.</param>
  void GuiActionExportStrings(IEnumerable<PartsRecord> parts, IEnumerable<AssemblyRecord> assemblies) {
    var partsLocalizations = parts
        .SelectMany(x => x.parts)
        .Select(Extractor.EmitItemsForPart)
        .SelectMany(x => x)
        .ToList();
    var assemblyRecords = assemblies as AssemblyRecord[] ?? assemblies.ToArray();
    var modulesLocalizations = assemblyRecords
        .SelectMany(x => x.assembly.GetTypes())
        .Select(Extractor.EmitItemsForType)
        .SelectMany(x => x)
        .ToList();
    DebugEx.Warning("Export {0} parts strings and {1} modules strings",
                    partsLocalizations.Count, modulesLocalizations.Count);
    var locItems = partsLocalizations.Union(modulesLocalizations);
    var fileName = "strings.cfg";
    if (assemblyRecords.Length == 1) {
      fileName = assemblyRecords.First().assembly.GetName().Name + "_" + fileName;
    }
    var filePath = KspPaths.GetModsDataFilePath(this, "Lang/" + fileName);
    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
    ConfigStore.WriteLocItems(locItems, Localizer.CurrentLanguage, filePath);
    DebugEx.Warning("Strings are written into: {0}", filePath);
    ShowCompletionDialog(StringsExportedDlgTitle, FileSavedTxt.Format(filePath.Replace("\\", "/")));
  }

  /// <summary>Finds all the entities for the prefix, and populates the list.</summary>
  /// <param name="prefix">The prefix to find URL by.</param>
  void GuiActionUpdateTargets(string prefix) {
    if (_targets == null) {
      _targets = new List<ScannedRecord>();
    } else {
      _targets.Clear();
    }
    if (prefix.Length < 3) {
      _targets.Add(new StubRecord() {
          stubText = TypePrefixToStartTxt,
      });
      return;
    }

    // Find part configs for the prefix.
    _targets.AddRange(
        PartLoader.LoadedPartsList
            .Where(x => x.partUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.partUrl)
            .GroupBy(x => {
              var pos = x.partUrl.LastIndexOf("/Parts", StringComparison.OrdinalIgnoreCase);
              return pos != -1 ? x.partUrl.Substring(0, pos + 6) : x.partUrl.Split('/')[0];
            })
            .Select(group => new PartsRecord() {
                urlPrefix = group.Key,
                parts = group.ToList(),
            }));

    // Find assemblies for the prefix.
    // Utility assemblies of the same version are loaded only once, but they are referred for every
    // URL at which the assembly was found.
    _targets.AddRange(
        AssemblyLoader.loadedAssemblies
            .Where(x =>
                 x.url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                 && KspPaths.MakeRelativePathToGameData(x.assembly.Location)
                     .StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                 && (_allowNoModulesAssemblies || x.types.Count > 0))
            .Select(assembly => new AssemblyRecord() {
                assembly = assembly.assembly,
                types = assembly.types.SelectMany(x => x.Value).ToList(),
            }));

    // Find localization files for the prefix.
    _targets.AddRange(
        GameDatabase.Instance.GetConfigs("Localization")
            .Where(x => x.url.StartsWith(_lookupPrefix, StringComparison.OrdinalIgnoreCase)
                       && x.config.GetNodes(Localizer.CurrentLanguage).Any())
            .Select(url => new ConfigRecord() {
                url = url.url,
                filePath = url.parent.fullPath,
                lang = Localizer.CurrentLanguage,
                node = url.config.GetNodes(Localizer.CurrentLanguage).FirstOrDefault(),
            }));

    if (_targets.Count == 0) {
      _targets.Add(new StubRecord() {
          stubText = NothingFoundForPrefixTxt,
      });
    }
  }

  /// <summary>Saves the strings for the selected entities into a new file.</summary>
  /// <param name="configs">The configs to update the localization strings for.</param>
  /// <param name="parts">The parts to update the string in.</param>
  void GuiActionRefreshStrings(IEnumerable<ConfigRecord> configs,
                               IEnumerable<PartsRecord> parts) {
    DebugEx.Warning("Update the selected part prefabs and strings due to the settings change");
    _defaultLocaleLookup = null;

    // Update the game's database with a fresh content from disk.
    configs.ToList().ForEach(
        x => LocalizationManager.UpdateLocalizationContent(x.filePath, x.node));

    // Update the part infos for the new language/content.
    var selectedParts = new HashSet<string>(
        parts.SelectMany(x => x.parts).Select(x => x.name));

    PartLoader.LoadedPartsList
        .Where(x => selectedParts.Contains(x.name))
        .ToList()
        .ForEach(LocalizationManager.LocalizePrefab);
    LocalizationManager.ReloadPartModuleStrings(selectedParts);

    // Notify listeners about the localization content changes.
    GameEvents.onLanguageSwitched.Fire();

    PartLoader.LoadedPartsList
        .Where(x => selectedParts.Contains(x.name))
        .ToList()
        .ForEach(LocalizationManager.LocalizePartInfo);

    // Update open part menus.
    LocalizationManager.LocalizePartMenus();

    // Force the localization methods to trigger on the refreshed prefab.
    GameEvents.onLanguageSwitched.Fire();
  }

  /// <summary>Triggers the part prefabs update.</summary>
  /// <remarks>This methods forces global language update.</remarks>
  void GuiActionUpdateAllParts() {
    DebugEx.Warning("Update all the part prefabs and strings due to the settings change");
    _defaultLocaleLookup = null;

    // Reload all the localization files.
    GameDatabase.Instance.GetConfigs("Localization")
        .Where(x => x.config.GetNodes(Localizer.CurrentLanguage).Any())
        .ToList()
        .ForEach(x => LocalizationManager.UpdateLocalizationContent(
            x.parent.fullPath, x.config.GetNodes(Localizer.CurrentLanguage).FirstOrDefault()));

    PartLoader.LoadedPartsList
        .ForEach(LocalizationManager.LocalizePrefab);
    LocalizationManager.ReloadPartModuleStrings();
    PartLoader.LoadedPartsList
        .ForEach(LocalizationManager.LocalizePartInfo);

    // Update open part menus.
    LocalizationManager.LocalizePartMenus();

    // Force refresh on all the parts and modules. This will also refresh the cached strings.
    Localizer.SwitchToLanguage(Localizer.CurrentLanguage);
  }

  /// <summary>
  /// Patches the part configs so that they refer the tags for the localizable fields, and saves the
  /// modified fields in the export location.
  /// </summary>
  /// <remarks></remarks>
  /// <param name="parts">The parts to patch.</param>
  void GuiExportPartConfigs(IEnumerable<PartsRecord> parts) {
    var exportParts = parts.SelectMany(x => x.parts).ToArray();
    var exportPath = KspPaths.GetModsDataFilePath(this, "Parts/");
    foreach (var part in exportParts) {
      var config = ConfigStore.LoadConfigWithComments(
          part.configFileFullName, localizeValues: false);
      if (config == null) {
        DebugEx.Error("Cannot load config file for part {0}: {1}", part, part.configFileFullName);
        continue;
      }

      // Make the default localizable placeholders for the known part fields.
      var partNode = config.GetNode("PART");
      if (partNode == null) {
        DebugEx.Error("Skipping part as it's config cannot be recognized: {0}", part.configFileFullName);
        continue;
      }
      foreach (var fieldName in Extractor.LocalizablePartFields) {
        var field = partNode.values.Cast<ConfigNode.Value>()
            .FirstOrDefault(x => x.name == fieldName);
        if (field == null) {
          DebugEx.Warning("Field '{0}' is not found in the part {1} config", fieldName, part);
          continue;
        }
        if (!LocalizationManager.IsLocalizationTag(field.value)) {
          // Replace a non-localized value by a tag.
          var locTag = Extractor.MakePartFieldLocalizationTag(part.name, fieldName);
          field.comment = new MetaBlock().SetInlineComment(locTag + " = " + field.value).ToString();
          field.value = locTag;
        } else {
          string locValue;
          if (Localizer.TryGetStringByTag(field.value, out locValue)) {
            // Update comment to the latest lang file.
            field.comment = new MetaBlock().SetInlineComment(field.value + " = " + locValue).ToString();
          }
        }
      }

      // Expand the localized placeholders to the default syntax format.
      ExpandLocalizedValues(config);

      var tgtPath = exportPath + part.name.Replace(".", "_") + ".cfg";
      DebugEx.Warning("Saving patched part config into: {0}", tgtPath);
      ConfigStore.SaveConfigWithComments(config, tgtPath);
    }
    ShowCompletionDialog(
        ConfigSavedDlgTitle,
        ConfigsSavedInFolderTxt.Format(exportParts.Length, exportPath.Replace("\\", "/")));
  }

  /// <summary>Changes the game's language and refreshes whatever possible.</summary>
  /// <remarks>The change is not persistent.</remarks>
  void GuiActionSetLanguage() {
    var oldLang = Localizer.CurrentLanguage;
    Localizer.SwitchToLanguage(_selectedLanguage);
    GuiActionUpdateTargets(_lookupPrefix);  // To update lang. 
    GuiActionUpdateAllParts();
    DebugEx.Warning("Changed language: {0} => {1}", oldLang, Localizer.CurrentLanguage);
  }

  /// <summary>Asynchronously calls the action and presents a standby dialog.</summary>
  /// <remarks>Use it when a lengthy blocking action needs to be executed.</remarks>
  /// <param name="fn">The action to execute.</param>
  /// <returns>The iterator to pass to <c>StartCoroutine</c>.</returns>
  IEnumerator ExecuteLongAction(Action fn) {
    var dlg = PopupDialog.SpawnPopupDialog(
        new MultiOptionDialog(ModalDialogId, WaitDialogText, WaitDialogTitle, skin: null),
        persistAcrossScenes: false, skin: null);
    yield return null;
    fn();
    yield return null;
    dlg.Dismiss();
  }

  /// <summary>Creates a simple modal dialog.</summary>
  /// <param name="title">The title of the dialog.</param>
  /// <param name="msg">The string to present in the dialog.</param>
  static void ShowCompletionDialog(string title, string msg) {
    PopupDialog dlg = null;
    dlg = PopupDialog.SpawnPopupDialog(
        new MultiOptionDialog(ModalDialogId, msg, title, null,
                              // ReSharper disable once PossibleNullReferenceException
                              // ReSharper disable once AccessToModifiedClosure
                              new DialogGUIButton(CloseDialogBtnTxt, () => dlg.Dismiss())),
        persistAcrossScenes: false, skin: null);
  }

  /// <summary>
  /// Recursively goes through the node fields and adds a stock-default comment for the localized field values.
  /// </summary>
  /// <param name="node">The parent node to start from.</param>
  void ExpandLocalizedValues(ConfigNode node) {
    foreach (var field in node.values.Cast<ConfigNode.Value>()) {
      if (LocalizationManager.IsLocalizationTag(field.value)
          && !LocalizationManager.IsSkippedTag(field.value)
          && string.IsNullOrEmpty(field.comment)) {
        // Make a default representation by adding EN-US strings as a comment.
        if (defaultLocaleLookup.Keys.Contains(field.value)) {
          field.comment = new MetaBlock()
              .SetInlineComment(field.value + " = " + defaultLocaleLookup[field.value])
              .ToString();
        } else {
          field.comment = new MetaBlock()
              .SetInlineComment(field.value + " is NOT found in EN-US locale!")
              .ToString();
        }
      }
    }
    foreach (var subNode in node.nodes.Cast<ConfigNode>()) {
      ExpandLocalizedValues(subNode);
    }
  }
}

[KSPAddon(KSPAddon.Startup.MainMenu, false /*once*/)]
class ControllerLauncher1 : MonoBehaviour {
  void Awake() {
    gameObject.AddComponent<Controller>();
  }
}

[KSPAddon(KSPAddon.Startup.FlightAndEditor, false /*once*/)]
class ControllerLauncher2 : MonoBehaviour {
  void Awake() {
    gameObject.AddComponent<Controller>();
  }
}

}  // namespace
