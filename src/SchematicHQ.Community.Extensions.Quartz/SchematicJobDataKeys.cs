using Quartz;

namespace SchematicHQ.Community.Extensions.Quartz;

/// <summary>
/// Job data map key prefixes read by the default <see cref="ISchematicJobContextResolver"/>. An entry
/// <c>schematic.company.id = acme</c> becomes company key <c>id = acme</c> in the flag context.
/// </summary>
public static class SchematicJobDataKeys
{
    public const string CompanyPrefix = "schematic.company.";
    public const string UserPrefix = "schematic.user.";
}

/// <summary>
/// Shortcuts for declaring the Schematic identity when building jobs and triggers, on both the standalone
/// builders and the configurators used inside <c>AddQuartz(q =&gt; q.AddJob(...))</c>.
/// </summary>
public static class SchematicJobBuilderExtensions
{
    public static JobBuilder<TJob> UsingSchematicCompany<TJob>(this JobBuilder<TJob> builder, string keyName, string value)
        where TJob : IJob
        => builder.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static JobBuilder<TJob> UsingSchematicUser<TJob>(this JobBuilder<TJob> builder, string keyName, string value)
        where TJob : IJob
        => builder.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);

    public static TriggerBuilder<TJob> UsingSchematicCompany<TJob>(this TriggerBuilder<TJob> builder, string keyName, string value)
        where TJob : IJob
        => builder.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static TriggerBuilder<TJob> UsingSchematicUser<TJob>(this TriggerBuilder<TJob> builder, string keyName, string value)
        where TJob : IJob
        => builder.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);

    public static IJobConfigurator<TJob> UsingSchematicCompany<TJob>(this IJobConfigurator<TJob> configurator, string keyName, string value)
        where TJob : IJob
        => configurator.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static IJobConfigurator<TJob> UsingSchematicUser<TJob>(this IJobConfigurator<TJob> configurator, string keyName, string value)
        where TJob : IJob
        => configurator.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);

    public static ITriggerConfigurator<TJob> UsingSchematicCompany<TJob>(this ITriggerConfigurator<TJob> configurator, string keyName, string value)
        where TJob : IJob
        => configurator.UsingJobData(SchematicJobDataKeys.CompanyPrefix + keyName, value);

    public static ITriggerConfigurator<TJob> UsingSchematicUser<TJob>(this ITriggerConfigurator<TJob> configurator, string keyName, string value)
        where TJob : IJob
        => configurator.UsingJobData(SchematicJobDataKeys.UserPrefix + keyName, value);
}
