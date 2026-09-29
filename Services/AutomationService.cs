using AudioFlow.Models;

namespace AudioFlow.Services;

/// <summary>First matching rule wins. A manual choice holds until the winning rule changes.</summary>
public sealed class AutomationService
{
    private Guid? _winner;
    public void Invalidate() => _winner = null;

    public AudioProfile? Evaluate(IEnumerable<AutomationRule> rules, IReadOnlyList<AudioProfile> profiles,
        IReadOnlyList<AudioDevice> devices, IReadOnlyCollection<string> processes)
    {
        var rule = rules.FirstOrDefault(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Match)
            && profiles.Any(p => p.Id == r.ProfileId) && (r.TriggerType switch
            {
                RuleTriggerType.ApplicationRunning => processes.Any(p =>
                    NormalizeProcess(p).Equals(NormalizeProcess(r.Match), StringComparison.OrdinalIgnoreCase)),
                RuleTriggerType.DeviceConnected => devices.Any(d =>
                    d.Name.Contains(r.Match.Trim(), StringComparison.OrdinalIgnoreCase)),
                _ => false
            }));
        if (rule?.Id == _winner) return null;
        _winner = rule?.Id;
        return rule is null ? null : profiles.First(p => p.Id == rule.ProfileId);
    }

    private static string NormalizeProcess(string value)
    {
        value = value.Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
    }
}
