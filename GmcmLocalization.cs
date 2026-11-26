using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace AndroidConsolizer
{
    /// <summary>Refresh the Mod's GMCM page when its language changes, retaining unsaved edits.</summary>
    internal sealed class GmcmLocalization
    {
        private readonly Dictionary<string, object> _pendingValues = new();
        private IClickableMenu _editingMenu;
        private FieldInfo _activeDropdown;
        private bool _refreshRequested;
        private bool _rebuilding;

        internal IManifest Manifest { get; }

        internal GmcmLocalization(IManifest manifest)
        {
            Manifest = new LocalizedManifest(manifest);
        }

        internal void RegisterFieldChanges(object api)
        {
            if (api == null)
                return;

            try
            {
                var register = AccessTools.Method(api.GetType(), "OnFieldChanged",
                    new[] { typeof(IManifest), typeof(Action<string, object>) });
                register?.Invoke(api, new object[] { Manifest, (Action<string, object>)OnFieldChanged });
            }
            catch
            {
                // Earlier GMCM versions still apply the language through the normal save callback.
            }
        }

        internal string GetText(string key, object tokens = null)
        {
            // Keep using the previewed language while this config menu remains open so
            // dynamic hover tooltips match the rebuilt labels before Save is pressed.
            string language = _editingMenu != null
                && _pendingValues.TryGetValue(nameof(ModConfig.Language), out object value)
                && value is string pending
                ? pending
                : I18n.Language;
            return I18n.Get(key, tokens, language);
        }

        internal T GetValue<T>(string field, T savedValue)
        {
            if (_rebuilding && _pendingValues.TryGetValue(field, out object value) && value is T pending)
                return pending;
            return savedValue;
        }

        internal T GetChoice<T>(string field, T savedValue) where T : struct, Enum
        {
            string value = GetValue<string>(field, null);
            return Enum.TryParse(value, out T choice) ? choice : savedValue;
        }

        private void OnFieldChanged(string field, object value)
        {
            if (_rebuilding)
                return;

            var menu = GetActiveMenu();
            if (menu?.GetType().FullName != "GenericModConfigMenu.Framework.SpecificModConfigMenu")
                return;

            if (!ReferenceEquals(menu, _editingMenu))
            {
                Clear();
                _editingMenu = menu;
            }

            _pendingValues[field] = value;
            if (field == nameof(ModConfig.Language))
            {
                // Some GMCM versions report provisional dropdown values while it is open.
                var dropdown = menu.GetType().Assembly.GetType("SpaceShared.UI.Dropdown");
                _activeDropdown = dropdown == null ? null : AccessTools.Field(dropdown, "ActiveDropdown");
                _refreshRequested = _activeDropdown?.IsStatic == true;
            }
        }

        internal void Clear()
        {
            _editingMenu = null;
            _activeDropdown = null;
            _refreshRequested = false;
            _pendingValues.Clear();
        }

        internal void Update()
        {
            if (_editingMenu == null)
                return;
            if (!ReferenceEquals(_editingMenu, GetActiveMenu()))
            {
                Clear();
                return;
            }
            if (!_refreshRequested)
                return;

            // GMCM copies the label/tooltip/choice callbacks into widgets when it constructs
            // the page. Recreate just this page after its input callback has finished.
            try
            {
                if (_activeDropdown?.GetValue(null) != null)
                    return;

                _refreshRequested = false;
                _rebuilding = true;
                var oldMenu = _editingMenu;
                var type = oldMenu.GetType();
                var config = AccessTools.Field(type, "ModConfig");
                var configValue = config?.GetValue(oldMenu);
                if (configValue == null)
                    return;
                var constructor = AccessTools.Constructor(type,
                    new[] { config.FieldType, typeof(int), typeof(string), typeof(Action<string>), typeof(Action) });
                if (constructor == null
                    || AccessTools.Field(type, "ScrollSpeed")?.GetValue(oldMenu) is not int scrollSpeed
                    || AccessTools.Field(type, "OpenPage")?.GetValue(oldMenu) is not Action<string> openPage
                    || AccessTools.Field(type, "ReturnToList")?.GetValue(oldMenu) is not Action returnToList)
                    return;

                // This Mod only registers a single, root page.
                var menu = (IClickableMenu)constructor.Invoke(new object[] { configValue, scrollSpeed, null, openPage, returnToList });
                if (ReferenceEquals(TitleMenu.subMenu, oldMenu))
                    TitleMenu.subMenu = menu;
                else if (oldMenu.GetParentMenu() is IClickableMenu parent)
                    parent.SetChildMenu(menu);
                else
                    Game1.activeClickableMenu = menu;
                _editingMenu = menu;
            }
            catch (Exception)
            {
                // Other GMCM versions can still apply the language on Save and read it on reopening.
                _refreshRequested = false;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private static IClickableMenu GetActiveMenu()
        {
            var menu = Game1.activeClickableMenu;
            if (menu is TitleMenu)
                menu = TitleMenu.subMenu;
            while (menu?.GetChildMenu() is IClickableMenu child)
                menu = child;
            return menu;
        }

        // GMCM also displays the manifest description in its mod list. Pass it a localized
        // view so it never renders the raw Chinese metadata using an English font.
        private sealed class LocalizedManifest : IManifest
        {
            private readonly IManifest _manifest;

            internal LocalizedManifest(IManifest manifest) => _manifest = manifest;

            public string Description => I18n.Get("ui.mod-description");
            public string Name => _manifest.Name;
            public string Author => _manifest.Author;
            public ISemanticVersion Version => _manifest.Version;
            public ISemanticVersion MinimumApiVersion => _manifest.MinimumApiVersion;
            public ISemanticVersion MinimumGameVersion => null; // This Mod's manifest does not declare it.
            public string UniqueID => _manifest.UniqueID;
            public string EntryDll => _manifest.EntryDll;
            public IManifestContentPackFor ContentPackFor => _manifest.ContentPackFor;
            public IManifestDependency[] Dependencies => _manifest.Dependencies;
            public string[] UpdateKeys => _manifest.UpdateKeys;
            public IDictionary<string, object> ExtraFields => _manifest.ExtraFields;
        }
    }
}
