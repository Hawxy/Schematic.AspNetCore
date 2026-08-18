using Microsoft.AspNetCore.Mvc;
using SchematicHQ.Community.AspNetCore;
using SchematicHQ.Community.AspNetCore.Attributes;

namespace SchematicHQ.Community.AspNetCore.TestApp;

[ApiController]
[Route("api/test")]
public sealed class TestController : ControllerBase
{
    [HttpGet("gate")]
    [RequireFeature(TestEndpoints.ControllerFlag)]
    public IActionResult Gate() => Ok(new { ok = true });

    [HttpGet("track")]
    [TrackFeature(TestEndpoints.ControllerTrackEvent, Quantity = 3)]
    public IActionResult Track() => Ok(new { ok = true });
}
