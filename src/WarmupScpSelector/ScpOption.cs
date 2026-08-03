using System;
using System.ComponentModel;
using PlayerRoles;

namespace WarmupScpSelector;

/// <summary>One SCP the warmup room offers: the role to grant, its label, and the model to display.</summary>
public sealed class ScpOption
{
    public ScpOption()
    {
    }

    public ScpOption(RoleTypeId role, string label, string model)
    {
        Role = role;
        Label = label;
        Model = model;
    }

    [Description("SCP role granted when this option is selected.")]
    public RoleTypeId Role { get; set; }

    [Description("Label shown above this SCP's model.")]
    public string Label { get; set; } = "SCP";

    [Description("Embedded model file basename, e.g. \"scp-173\" loads scp-173.mer.json. Leave blank to use the built-in model for known SCP roles.")]
    public string Model { get; set; } = "";

    /// <summary>True if the role is an SCP role (its enum name starts with "Scp").</summary>
    public static bool IsScpRole(RoleTypeId role) => role.ToString().StartsWith("Scp", StringComparison.Ordinal);
}
