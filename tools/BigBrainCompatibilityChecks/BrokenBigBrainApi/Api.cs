namespace DrakiaXYZ.BigBrain.Brains;

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
    // AddCustomLayer deliberately does not exist. This exercises a real method
    // resolution/JIT failure at the optional boundary, not a thrown test error.
}
