// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using KSP.Localization;
using KSPDev.FSUtils;
using KSPDev.ConfigUtils;
using KSPDev.GUIUtils;
using KSPDev.LogUtils;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KSPDev.LocalizationTool {

[PersistentFieldsFileAttribute("KSPDev/LocalizationTool/PluginData/settings.cfg", "UI")]
[PersistentFieldsFileAttribute("KSPDev/LocalizationTool/PluginData/session.cfg", "UI",
                               Controller.SessionGroup)]
class Controller : MonoBehaviour, IHasGUI {
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

  readonly static Message MakeSelectionsForPatchTxt = new Message(
      "#locTool_00010",
      "<i>EXPORT PART CONFIGS: Select a parts folder</i>");

  readonly static Message<int> PatchPartsBtnTxt = new Message<int>(
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
  #endregion

  #region GUI scrollbox records
  /// <summary>Base class for the records that represent the extractor entities.</summary>
  abstract class ScannedRecord {
    public bool selected;
    public virtual void GUIAddItem() {
      selected = GUILayout.Toggle(selected, ToString());
    }
  }

  /// <summary>Simple item to display info in the scroll box. It cannot be selected.</summary>
  class StubRecord : ScannedRecord {
    public string stubText;
    public override void GUIAddItem() {
      GUILayout.Label(stubText);
    }
  }

  /// <summary>Item that represents mod's parts record.</summary>
  class PartsRecord : ScannedRecord {
    public List<AvailablePart> parts = new List<AvailablePart>();
    public string urlPrefix = "";

    /// <inheritdoc/>
    public override string ToString() {
      return string.Format("{0} ({1} parts)", urlPrefix, parts.Count);
    }
  }

  /// <summary>Item that represents an assembly record.</summary>
  class AssemblyRecord : ScannedRecord {
    public Assembly assembly;
    public List<Type> types;
    public string url;

    /// <inheritdoc/>
    public override string ToString() {
      return string.Format("{0}, v{1} ({2} modules)",
                           KspPaths.MakeRelativePathToGameData(assembly.Location),
                           assembly.GetName().Version, types.Count);
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
      return string.Format(
          "{0}, lang={1} ({2} strings)",
          KspPaths.MakeRelativePathToGameData(url), lang, node.GetValues().Length);
    }
  }
  #endregion

  /// <summary>Name of the persistent group to keep session settings in.</summary>
  /// <remarks>
  /// Session keeps current UI and layout settings. They get changed frequently and saved/loaded on
  /// every scene.
  /// </remarks>
  const string SessionGroup = "session";

  #region Mod's settings
  [PersistentField("toggleConsoleKey")]
  static string toggleConsoleKey = "&f8";

  [PersistentField("scrollHeight")]
  static int scrollHeight = 150;
  #endregion

  #region Session settings
  [PersistentField("windowPos", group = SessionGroup)]
  static Vector2 windowPos = new Vector2(0, 0);

  /// <summary>Specifies if debug console is visible.</summary>
  [PersistentField("isOpen", group = SessionGroup)]
  static bool isUIVisible;

  [PersistentField("lookupPrefix", group = SessionGroup)]
  string lookupPrefix = "";

  [PersistentField("showNoModulesAssemblies", group = SessionGroup)]
  bool allowNoModulesAssemblies;
  #endregion

  /// <summary>A list of actions to apply at the end of the GUI frame.</summary>
  static readonly GuiActionsList guiActions = new GuiActionsList();

  #region Window intermediate properties
  static Vector2 windowSize = new Vector2(430, 0);
  static Rect windowRect;
  #endregion

  List<ScannedRecord> targets;
  Vector2 partsScrollPos;
  string lastCachedLookupPrefix;
  Event toggleConsoleKeyEvent;
  PopupDialog currentDialog;

  #region MonoBehaviour overrides 
  /// <summary>Only loads session settings.</summary>
  void Awake() {
    ConfigAccessor.ReadFieldsInType(typeof(Controller), null /* instance */);
    ConfigAccessor.ReadFieldsInType(typeof(Controller), this, group: SessionGroup);
    toggleConsoleKeyEvent = Event.KeyboardEvent(toggleConsoleKey);
    windowRect = new Rect(windowPos, windowSize);
  }

  /// <summary>Only stores session settings.</summary>
  void OnDestroy() {
    windowPos = windowRect.position;
    ConfigAccessor.WriteFieldsFromType(typeof(Controller), this, group: SessionGroup);
  }
  #endregion

  #region IHasGUI implementation
  /// <inheritdoc/>
  public void OnGUI() {
    if (Event.current.Equals(toggleConsoleKeyEvent)) {
      Event.current.Use();
      isUIVisible = !isUIVisible;
      targets = null;
    }
    if (isUIVisible) {
      if (targets == null) {
        GuiActionUpdateTargets(lookupPrefix);
      }
      windowRect = GUILayout.Window(
          0, windowRect, MakeConsoleWindow,
          MainWindowTitleTxt.Format(GetType().Assembly.GetName().Version));
    }
  }
  #endregion

  /// <summary>Shows a UI dialog.</summary>
  /// <param name="windowID">The window ID. Unused.</param>
  void MakeConsoleWindow(int windowID) {
    guiActions.ExecutePendingGuiActions();
    // Search prefix controls.
    using (new GUILayout.HorizontalScope(GUI.skin.box)) {
      GUILayout.Label(UrlPrefixFieldCaptionTxt, GUILayout.ExpandWidth(false));
      lookupPrefix = GUILayout.TextField(lookupPrefix, GUILayout.ExpandWidth(true)).TrimStart();
      if (lookupPrefix != lastCachedLookupPrefix) {
        lastCachedLookupPrefix = lookupPrefix;
        guiActions.Add(() => GuiActionUpdateTargets(lookupPrefix));
      }
    }

    // Found items scroll view.
    using (var scrollScope = new GUILayout.ScrollViewScope(
        partsScrollPos, GUI.skin.box, GUILayout.Height(scrollHeight))) {
      partsScrollPos = scrollScope.scrollPosition;
      foreach (var target in targets) {
        target.GUIAddItem();
      }
    }

    GUI.changed = false;
    allowNoModulesAssemblies =
        GUILayout.Toggle(allowNoModulesAssemblies, AssembliesWithoutModulesToggleTxt);
    if (GUI.changed) {
      guiActions.Add(() => GuiActionUpdateTargets(lookupPrefix));
    }

    // Action buttons.
    var selectedModulesCount = targets.OfType<AssemblyRecord>()
        .Where(x => x.selected)
        .Sum(x => x.types.Count);
    var selectedPartsCount = targets.OfType<PartsRecord>()
        .Where(x => x.selected)
        .Sum(x => x.parts.Count);
    var selectedLocsCount = targets.OfType<ConfigRecord>()
        .Count(x => x.selected);

    var selectedAssemblies = targets.OfType<AssemblyRecord>().Where(x => x.selected);
    var selectedParts = targets.OfType<PartsRecord>().Where(x => x.selected);
    var selectedConfigs = targets.OfType<ConfigRecord>().Where(x => x.selected);

    // Strings export controls.
    if (selectedPartsCount > 0
        || allowNoModulesAssemblies && selectedAssemblies.Any()
        || !allowNoModulesAssemblies && selectedModulesCount > 0) {
      var title = ExportBtnTxt.Format(selectedParts.Sum(x => x.parts.Count),
                                      selectedAssemblies.Count());
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
    if (selectedLocsCount > 0) {
      var title = RefreshBtnTxt.Format(selectedConfigs.Count(),
                                       selectedParts.Sum(x => x.parts.Count));
      if (GUILayout.Button(title)) {
        GuiActionRefreshStrings(selectedConfigs, selectedParts);
      }
    } else {
      GUI.enabled = false;
      GUILayout.Button(MakeSelectionsForReloadTxt);
      GUI.enabled = true;
    }

    // Parts DB update controls.
    if (GUILayout.Button(UpdateAllPartsTxt)) {
      GuiActionUpdateAllParts();
    }

    GUI.DragWindow();
  }

  /// <summary>Saves the strings for the selected entities into a new file.</summary>
  /// <param name="parts">The parts to export the strings from.</param>
  /// <param name="assemblies">The mod assemblies to export teh strinsg from.</param>
  void GuiActionExportStrings(IEnumerable<PartsRecord> parts,
                              IEnumerable<AssemblyRecord> assemblies) {
    var partsLocs = parts
        .SelectMany(x => x.parts)
        .Select(Extractor.EmitItemsForPart)
        .SelectMany(x => x)
        .ToList();
    var modulesLocs = assemblies
        .SelectMany(x => x.assembly.GetTypes())
        .Select(Extractor.EmitItemsForType)
        .SelectMany(x => x)
        .ToList();
    DebugEx.Warning(
        "Export {0} parts strings and {1} modules strings", partsLocs.Count, modulesLocs.Count);
    var locItems = partsLocs.Union(modulesLocs);
    var fileName = "strings.cfg";
    if (assemblies.Count() == 1) {
      fileName = assemblies.First().assembly.GetName().Name + "_" + fileName;
    }
    var filePath = KspPaths.GetModsDataFilePath(this, "Lang/" + fileName);
    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
    ConfigStore.WriteLocItems(locItems, Localizer.CurrentLanguage, filePath);
    DebugEx.Warning("Strings are written into: {0}", filePath);
    ShowCompletionDialog(StringsExportedDlgTitle, FileSavedTxt.Format(filePath));
  }

  /// <summary>Saves the strings for the selected entities into a new file.</summary>
  /// <param name="configs">The configs to update the localization strings for.</param>
  /// <param name="parts">The parts to update the string in.</param>
  void GuiActionRefreshStrings(IEnumerable<ConfigRecord> configs,
                               IEnumerable<PartsRecord> parts) {
    // Updatate game's database with a fresh content from the disk.
    configs.ToList().ForEach(
        x => LocalizationManager.UpdateLocalizationContent(x.filePath, x.node));

    // Notify listeners about the localization content changes.
    GameEvents.onLanguageSwitched.Fire();

    // Update the part infos for the new language/content.
    var selectedParts = new HashSet<string>(
        parts.SelectMany(x => x.parts).Select(x => x.name));
    PartLoader.LoadedPartsList
        .Where(x => selectedParts.Contains(x.name))
        .ToList()
        .ForEach(LocalizationManager.LocalizePartInfo);

    // Update open part menus.
    LocalizationManager.LocalizePartMenus();
  }

  /// <summary>Finds all the entities for the prefix, and populates the list.</summary>
  /// <param name="prefix">The prefix to find URL by.</param>
  void GuiActionUpdateTargets(string prefix) {
    if (targets == null) {
      targets = new List<ScannedRecord>();
    } else {
      targets.Clear();
    }
    if (prefix.Length < 3) {
      targets.Add(new StubRecord() {
          stubText = TypePrefixToStartTxt,
      });
      return;
    }

    // Find part configs for the prefix.
    targets.AddRange(PartLoader.LoadedPartsList
        .Where(x => x.partUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.partUrl)
        .GroupBy(x => {
            var pos = x.partUrl.LastIndexOf("/Parts", StringComparison.OrdinalIgnoreCase);
            return pos != -1 ? x.partUrl.Substring(0, pos + 6) : x.partUrl.Split('/')[0];
        })
        .Select(group => new PartsRecord() {
            urlPrefix = group.Key,
            parts = group.ToList(),
        })
        .Cast<ScannedRecord>());

    // Find assemblies for the prefix.
    // Utility assemblies of the same version are loaded only once, but they are referred for every
    // URL at which the assembly was found.
    targets.AddRange(AssemblyLoader.loadedAssemblies
        .Where(x =>
            x.url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && KspPaths.MakeRelativePathToGameData(x.assembly.Location)
                .StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (allowNoModulesAssemblies || x.types.Count > 0))
        .Select(assembly => new AssemblyRecord() {
            assembly = assembly.assembly,
            types = assembly.types.SelectMany(x => x.Value).ToList(),
            url = assembly.url,
        })
        .Cast<ScannedRecord>());

    // Find localization files for the prefix.
    targets.AddRange(GameDatabase.Instance.GetConfigs("Localization")
        .Where(x => x.url.StartsWith(lookupPrefix, StringComparison.OrdinalIgnoreCase)
                    && x.config.GetNodes(Localizer.CurrentLanguage).Any())
        .Select(url => new ConfigRecord() {
            url = url.url,
            filePath = url.parent.fullPath,
            lang = Localizer.CurrentLanguage,
            node = url.config.GetNodes(Localizer.CurrentLanguage).FirstOrDefault(),
        })
        .Cast<ScannedRecord>());

    if (targets.Count == 0) {
      targets.Add(new StubRecord() {
          stubText = NothingFoundForPrefixTxt,
      });
    }
  }

  /// <summary>Triggers the part prefabs update.</summary>
  void GuiActionUpdateAllParts() {
    // Force all strings to recalculate in case of they were cached.
    GameEvents.onLanguageSwitched.Fire();
    DebugEx.Warning("Update all the part prefabs due to the settings change");
    PartLoader.LoadedPartsList
        .ForEach(LocalizationManager.LocalizePartInfo);
    LocalizationManager.LocalizePartMenus();
  }

  /// <summary>
  /// Patches the part configs so that they refer the tags for the localizable fileds, and saves the
  /// modified fiels in the export location.
  /// </summary>
  /// <remarks></remarks>
  /// <param name="parts">The parts to patch.</param>
  void GuiExportPartConfigs(IEnumerable<PartsRecord> parts) {
    var exportParts = parts.SelectMany(x => x.parts);
    var exportPath = KspPaths.GetModsDataFilePath(this, "Parts/");
    foreach (var part in exportParts) {
      var config = ConfigStore.LoadConfigWithComments(
          part.configFileFullName, localizeValues: false);
      if (config == null) {
        DebugEx.Error("Cannot load config file for part {0}: {1}", part, part.configFileFullName);
        continue;
      }
      var partNode = config.GetNode("PART");
      foreach (var fieldName in Extractor.localizablePartFields) {
        var field = partNode.values.Cast<ConfigNode.Value>()
            .FirstOrDefault(x => x.name == fieldName);
        if (field == null) {
          DebugEx.Warning("Field '{0}' is not found in the part {1} config", fieldName, part);
          continue;
        }
        if (field.value.StartsWith("#", StringComparison.Ordinal)) {
          continue;  // It's already localized.
        }
        var locTag = Extractor.MakePartFieldLocalizationTag(part.name, fieldName);
        field.comment = locTag + " = " + field.value;
        field.value = locTag;
      }

      var tgtPath = exportPath + part.name.Replace(".", "_") + ".cfg";
      DebugEx.Warning("Saving patched part config into: {0}", tgtPath);
      ConfigStore.SaveConfigWithComments(config, tgtPath);
    }
    ShowCompletionDialog(
        ConfigSavedDlgTitle,
        ConfigsSavedInFolderTxt.Format(exportParts.Count(), exportPath));
  }

  /// <summary>Creates a simple modal dialog.</summary>
  /// <param name="title">The title of the dialog.</param>
  /// <param name="msg">The string to present in the dialog.</param>
  void ShowCompletionDialog(string title, string msg) {
    if (currentDialog != null) {
      currentDialog.Dismiss();
    }
    currentDialog = PopupDialog.SpawnPopupDialog(
        new MultiOptionDialog(
            "StringsExportedDlg",
            msg,
            title,
            null,
            new DialogGUIButton(CloseDialogBtnTxt, () => {
              currentDialog.Dismiss();
              currentDialog = null;
            })),
        false, null);
  }
}

[KSPAddon(KSPAddon.Startup.MainMenu, false /*once*/)]
class ControllerLauncher1 : Controller {
}

[KSPAddon(KSPAddon.Startup.FlightAndEditor, false /*once*/)]
class ControllerLauncher2 : Controller {
}

}  // namesapce
