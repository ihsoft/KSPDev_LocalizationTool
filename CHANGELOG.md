# 1.10 (pre-release):
* [Fix #16] Bad escaping in the localization makes the tool crashing.
* [Fix #17] Support the stock fields/events/actions GUI strings reloading.
* [Enhancement] Support the game's UI scale.

# 1.9 (April 26h, 2020):
* [Change] `KSP 1.8` compatibility. __WARNING__: the mod won't work with version lower than `KSP 1.8`!

# 1.8 (March 25h, 2019):
* [Change] Add localizations to the missed strings in the mod.
* [Enhancement] Add standby dialog for the action that can take much time to complete.
* [Enhancement] Add RU localization.
* [Enhancement] Support "global strings". They can be used multiple times across the mod.
* [Enhancement #12] Properly handle color values.
* [Enhancement #13] Exclude the stock localization tags from the exported files.
* [Enhancement #14] Add ability to switch the current game language in runtime.

# 1.7 (December 24th, 2018):
* [Change] KSP 1.6 compatibility.

# 1.6 (October 16th, 2018):
* [Change] KSP 1.5 compatibility.

# 1.5 (September 27th, 2018):
* [Fix #7] Empty one-liner node declaration absorbs all nodes downstream.
* [Enhancement #9] Expand localization tags to the default syntax on patched export.
* [Enhancement] Report an error if a config file cannot be properly parsed.

# 1.4 (July 9th, 2018):
* [Change] Include language files in the installation package.
* [Change] Migrate to Utils 0.37. Stability fix.
* [Change] Update EN-us localization file.
* [Enhancement] Add Spanish localization (ES_es).
* [Enhancement] Update all the localization files in the game when "Update all parts" is selected.
* [Enhancement] Reload the stock `KSPField` on the strings refresh action.
* [Enhancement] Export strings with respect to the currently loaded lang file.

# 1.3 (July 7th, 2018):
* Improve the strings refresh algorythm to cover cases when the part info depends on the config.
* Invalidate menus to have `UI_Control` fields updated.
* Support more markup cases in the config node parser.
* Improve handling multi-part configs and MM pacthes in the part configs.
* Migrate to Utils 0.36.
* [Fix] Use unique ID for the GUI dialog to not conflict with the other game dialogs.
* [Fix #6] Make the rules on how the part fields get sorted.

# 1.2 (June 25th, 2018):
* Support tags extraction from non-standard part fields.
* Support structured fields extraction.
* Reload localizable strings in the prefab configs.
* Migrate to Utils 0.35.

# 1.1 (March 7th, 2018):
* KSP 1.4.0 compatibility.
* Migrate to Utils 0.31.
* Add the string export completion dialog to show the final path.
* Improve handling of the non-standard localized part fields.

# 1.0 (September 25th, 2017):
* Initial public version.
