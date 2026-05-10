using Microsoft.AspNetCore.Mvc;
using Backend.DTOs;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/v1/internal")]
public class InternalController : ControllerBase
{
    private readonly IParkingIngestService _ingest;

    public InternalController(IParkingIngestService ingest)
    {
        _ingest = ingest;
    }

    [HttpPost("events")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PostEvent([FromBody] EventRequest request)
    {
        await _ingest.HandleEventAsync(request, HttpContext.RequestAborted);
        return Ok(new { ok = true });
    }

    [HttpPost("gateway-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PostGatewayStatus([FromBody] GatewayStatusRequest request)
    {
        await _ingest.HandleGatewayStatusAsync(request, HttpContext.RequestAborted);
        return Ok(new { ok = true });
    }
}