using Schematic.AspNetCore;
using Schematic.AspNetCore.TestApp;
using Schematic.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting();
builder.Services.AddControllers();
builder.Services.AddSchematic("test-api-key");
builder.Services.AddSchematicAspNetCore(o => o.WebhookSecret = TestEndpoints.WebhookSecret);

var app = builder.Build();

app.MapGroup(string.Empty).AddSchematicFilters().MapTestEndpoints();
app.MapControllers().AddSchematicFilters();

app.Run();
