namespace Editor.Tools.AssetIntegrator
{
    public interface IIntegrationStep
    {
        string StepName { get; }
        StepStatus Status { get; }
        string Log { get; }
        bool IsEnabled { get; set; }
        bool AutoFix { set; }
        void Validate();
        void ApplyFix();
        void Reset();
    }
}