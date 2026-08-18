using Quartz;

namespace Schematic.Extensions.Quartz;

/// <summary>
/// Job data map key prefixes read by the default <see cref="ISchematicJobContextResolver"/>. An entry
/// <c>schematic.company.id = acme</c> becomes company key <c>id = acme</c> in the flag context.
/// </summary>
public static class SchematicJobDataKeys
{
    public const string CompanyPrefix = "schematic.company.";
    public const string UserPrefix = "schematic.user.";
}

/// <summary>Shortcuts for declaring the Schematic identity when building jobs and triggers.</summary>
public static class SchematicJobBuilderExtensions
{
    public static JobBuilder UsingSchematicCompany(this JobBuilder builder, string keyName, string value)
        => builder.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static JobBuilder UsingSchematicUser(this JobBuilder builder, string keyName, string value)
        => builder.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);

    public static TriggerBuilder UsingSchematicCompany(this TriggerBuilder builder, string keyName, string value)
        => builder.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static TriggerBuilder UsingSchematicUser(this TriggerBuilder builder, string keyName, string value)
        => builder.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);
}
