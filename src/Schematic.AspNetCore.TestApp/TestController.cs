using Microsoft.AspNetCore.Mvc;
using Schematic.AspNetCore;
using Schematic.AspNetCore.Attributes;

namespace Schematic.AspNetCore.TestApp;

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
