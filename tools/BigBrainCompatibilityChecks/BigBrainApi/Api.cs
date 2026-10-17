namespace DrakiaXYZ.BigBrain.Brains;

// A separate assembly is essential: a stub in the probe assembly could not
// detect an accidental eager BigBrain type reference in the plugin bootstrap.
public abstract class CustomLayer { }

public static class BrainManager
{
    public sealed class LayerInfo
    {
        public Type customLayerType;
        public int customLayerPriority;
        public List<string> CustomLayerBrains;
    }

    public static readonly Dictionary<int, LayerInfo> CustomLayersReadOnly = new();
    public static string Scenario;
    public static int Calls;

    public static int AddCustomLayer(Type type, List<string> brains, int priority)
    {
        int id = ++Calls;
        if (Scenario == "api-failure" && id == 2)
            throw new MissingMethodException("Synthetic future API incompatibility");
        if (Scenario == "missing-registration" && id == 3) return id;
        CustomLayersReadOnly.Add(id, new LayerInfo
        {
            customLayerType = Scenario == "wrong-type" && id == 3 ? typeof(string) : type,
            customLayerPriority = Scenario == "wrong-priority" && id == 2 ? -1 : priority,
            CustomLayerBrains = Scenario == "missing-brain" && id == 1 ? new() : new(brains)
        });
        return id;
    }
}
