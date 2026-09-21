using ArenaMode.Config;
using ArenaMode.Shared;
using JsonType;
using UnityEngine;

namespace EFT
{
    public enum ESideType { Pmc, Savage }
    public enum EDateTime { CURR, PAST }
    [Flags] public enum ELocationTransition { None = 0, Common = 1, Event = 2 }
    public sealed class RaidSettings
    {
        public ESideType Side;
        public bool IsPmc => Side == ESideType.Pmc;
        public LocationSettings.Location SelectedLocation;
        public EDateTime SelectedDateTime;
        public ELocationTransition transitionType;
        public string KeyId;
    }
    public sealed class LocalRaidSettings
    {
        public string location;
        public ESideType playerSide;
        public ELocationTransition transitionType;
    }
}
namespace JsonType
{
    public sealed class LocationSettings
    {
        public sealed class Location
        {
            public string Id;
            public bool Enabled = true;
            public bool Locked;
        }
    }
}
namespace EFT.UI
{
    public sealed class TestToggle
    {
        private bool _value;
        public Action<bool> Changed;
        public bool IsToggled
        {
            get => _value;
            set { if (_value == value) return; _value = value; Changed?.Invoke(value); }
        }
    }
    public sealed class TestSpawnableToggle { public readonly TestToggle Toggle = new(); }
    public sealed class NativeButton : Component { public TMPro.TMP_Text _headerLabel = new(); }
    public sealed class NativeConditions
    {
        public int CloseCalls;
        public bool Open;
        public void Close() { CloseCalls++; Open = false; }
    }
    public sealed class LocationButton : Component
    {
        public LocationSettings.Location Location;
        public readonly TestSpawnableToggle _spawnableToggle = new();
        public void Show(LocationSettings.Location location, ESideType side, bool onAtStart, int level,
            bool overloaded, bool isNew, Action<LocationSettings.Location> onSelected, bool special, bool boss)
        {
            object[] args = [location, onAtStart, onSelected];
            NativeHooks.Call("ButtonLocationPatch", "Prefix", args);
            location = (LocationSettings.Location)args[0]; onAtStart = (bool)args[1];
            Location = location;
            gameObject.SetActive(true);
            _spawnableToggle.Toggle.Changed = null;
            _spawnableToggle.Toggle.IsToggled = onAtStart;
            var captured = location;
            // Native LocationButton.cs:102-108 invokes only on switching ON.
            _spawnableToggle.Toggle.Changed = active => { if (active) onSelected(captured); };
            NativeHooks.Call("ButtonLocationPatch", "Postfix", this, location, onSelected);
        }
        public void Select(bool value) => _spawnableToggle.Toggle.IsToggled = true;
    }
}
namespace EFT.UI.Matchmaker
{
    public sealed class LocationPath : Component { }

    /// <summary>
    /// Callback-order host, derived from native methods at lines 315-368,
    /// 398-470 and 512-583. Network/profile rendering and group level policy are
    /// deliberately absent; the critical non-null dereference is retained.
    /// </summary>
    public sealed class MatchMakerSelectionLocationScreen : MonoBehaviour
    {
        public sealed class SelectionLocationScreenController
        {
            public RaidSettings RaidSettings;
            public void UpdateSelectedDateTime()
            { if (RaidSettings.SelectedLocation == null) RaidSettings.SelectedDateTime = EDateTime.CURR; }
        }
        public SelectionLocationScreenController ScreenController;
        public Transform _locationPathsContainer, _locationButtonsContainer;
        public NativeButton _marathonButton, _acceptButton, _readyButton;
        public NativeButton _infoPanel;
        public readonly NativeConditions _conditions = new();
        public readonly Dictionary<string, LocationSettings.Location> Locations = new(StringComparer.Ordinal);
        public readonly List<LocationButton> Buttons = new();
        public int ShowCalls, SubscriptionCount, NonNullSelectionBranches, NativeDereferences, MaxSelectionDepth;
        public string LastNativeDereference;
        private int _selectionDepth;
        private RaidSettings _raidSettings;
        private EDateTime _selectedDateTime;
        private LocationSettings.Location _selectedLocation;

        public void Initialize(RaidSettings settings)
        {
            ScreenController = new() { RaidSettings = settings };
            _raidSettings = settings; _selectedDateTime = settings.SelectedDateTime;
            var container = new GameObject("Shared native map paths and buttons");
            container.transform.SetParent(transform, false);
            _locationPathsContainer = _locationButtonsContainer = container.transform;
            _marathonButton = ChildButton("Marathon");
            _acceptButton = ChildButton("Next"); _readyButton = ChildButton("Ready");
            _infoPanel = ChildButton("Native location information");
            foreach (string map in new[] { ArenaMapCatalog.FactoryDayId, ArenaMapCatalog.FactoryNightId,
                         ArenaMapCatalog.CustomsId, ArenaMapCatalog.LaboratoryId, ArenaMapCatalog.LabyrinthId,
                         ArenaMapCatalog.EquatorId, ArenaMapCatalog.Bay5Id, "Sandbox" })
                Locations[map] = new() { Id = map, Enabled = map != ArenaMapCatalog.FactoryNightId && map != ArenaMapCatalog.LabyrinthId };
        }
        private NativeButton ChildButton(string name)
        { var root = new GameObject(name); root.transform.SetParent(transform, false); return root.AddComponent<NativeButton>(); }

        public LocationSettings.Location SelectedLocation
        {
            get => _selectedLocation;
            set
            {
                object[] args = [this, value];
                NativeHooks.Call("SelectedValuePatch", "Prefix", args);
                value = (LocationSettings.Location)args[1];
                NativeHooks.Trace.Add("native-set:" + (value?.Id ?? "null") + ":" + Settings.TeamDeathmatchArea.Value);
                _selectedLocation = value;
                if (value?.Id is ArenaMapCatalog.FactoryDayId or ArenaMapCatalog.LaboratoryId) _selectedDateTime = EDateTime.CURR;
                else if (value?.Id == ArenaMapCatalog.FactoryNightId) _selectedDateTime = EDateTime.PAST;
                _raidSettings.SelectedLocation = value;
                _raidSettings.SelectedDateTime = _selectedDateTime;
                ScreenController.UpdateSelectedDateTime();
                bool ready = value != null && !value.Locked;
                _acceptButton.gameObject.SetActive(ready); _readyButton.gameObject.SetActive(ready);
                if (!ready) _conditions.Close();
            }
        }

        public void ShowInternal(RaidSettings settings)
        {
            NativeHooks.Call("ShowPatch", "Prefix", this, settings);
            ShowCalls++;
            OnLocationSelected(settings.SelectedLocation);
            var path = new GameObject("Native path"); path.transform.SetParent(_locationPathsContainer, false); path.AddComponent<LocationPath>();
            foreach (var location in Locations.Values.Where(DisplayLocation))
            {
                var root = new GameObject("Native map " + location.Id); root.transform.SetParent(_locationButtonsContainer, false);
                var button = root.AddComponent<LocationButton>(); Buttons.Add(button);
                button.Show(location, settings.Side, ReferenceEquals(location, SelectedLocation), 30, false, false,
                    OnLocationSelected, false, false);
            }
            // Native ShowInternal appends, and has no corresponding disposal.
            SubscriptionCount += 2;
            NativeHooks.Call("ShowPatch", "Postfix", this);
        }

        public bool DisplayLocation(LocationSettings.Location location)
        {
            object[] args = [this, location, location.Enabled && location.Id != ArenaMapCatalog.FactoryNightId];
            NativeHooks.Call("DisplayPatch", "Postfix", args);
            return (bool)args[2];
        }

        public void OnLocationSelected(LocationSettings.Location location)
        {
            _selectionDepth++; MaxSelectionDepth = Math.Max(MaxSelectionDepth, _selectionDepth);
            if (_selectionDepth > 8) throw new InvalidOperationException("Recursive native OnLocationSelected.");
            try
            {
                object[] args = [this, location];
                NativeHooks.Call("SelectionPatch", "Prefix", args);
                location = (LocationSettings.Location)args[1];
                NativeHooks.Trace.Add("native-select:" + (location?.Id ?? "null"));
                if (location == null) { SelectedLocation = null; return; }
                NonNullSelectionBranches++;
                var actual = GetLocationByTime(location, _raidSettings.SelectedDateTime);
                actual = GetLocationByLevel(actual);
                SelectedLocation = actual;
                // Preserve this native line, including its failure if our
                // prefix rejects only after native chose the non-null branch.
                LastNativeDereference = SelectedLocation.Id;
                NativeDereferences++;
                _conditions.Open = SelectedLocation.Id != ArenaMapCatalog.LaboratoryId;
            }
            finally
            {
                _selectionDepth--;
                NativeHooks.Call("SelectionPatch", "Postfix", this);
            }
        }

        public LocationSettings.Location GetLocationByTime(LocationSettings.Location location, EDateTime time)
        {
            object[] args = [this, location, time];
            NativeHooks.Call("FactoryTimePatch", "Prefix", args);
            time = (EDateTime)args[2];
            var result = location.Id is ArenaMapCatalog.FactoryDayId or ArenaMapCatalog.FactoryNightId
                ? Locations[time == EDateTime.CURR ? ArenaMapCatalog.FactoryDayId : ArenaMapCatalog.FactoryNightId] : location;
            NativeHooks.Trace.Add("native-time:" + result.Id);
            return result;
        }
        public LocationSettings.Location GetLocationByLevel(LocationSettings.Location location)
            => location == null || location.Id == "Sandbox" ? Locations["Sandbox"] : location;

        public void UpdateSelectedTime(EDateTime time)
        {
            _selectedDateTime = time; _raidSettings.SelectedDateTime = time;
            ScreenController.UpdateSelectedDateTime();
            SelectedLocation = GetLocationByTime(_raidSettings.SelectedLocation, _selectedDateTime);
        }
        public void GroupRefresh() => SelectedLocation = GetLocationByLevel(SelectedLocation);
        public void Close()
        {
            NativeHooks.Call("ClosePatch", "Prefix", this);
            foreach (var button in Buttons) UnityEngine.Object.Destroy(button.gameObject);
            Buttons.Clear(); SubscriptionCount = 0;
            foreach (var path in _locationPathsContainer.GetComponentsInChildren<LocationPath>(true)) UnityEngine.Object.Destroy(path.gameObject);
            _conditions.Close();
        }
    }
}
