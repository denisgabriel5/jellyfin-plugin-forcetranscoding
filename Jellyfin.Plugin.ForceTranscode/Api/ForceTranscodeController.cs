using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ForceTranscode.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ForceTranscode.Api;

/// <summary>
/// REST API for managing force-transcode profiles.
/// All endpoints require admin elevation.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("ForceTranscode")]
[Produces("application/json")]
public class ForceTranscodeController : ControllerBase
{
    /// <summary>
    /// Returns all configured force-transcode profiles.
    /// </summary>
    [HttpGet("Profiles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<ForceTranscodeProfile>> GetProfiles()
        => Ok(Plugin.Instance!.Configuration.Profiles);

    /// <summary>
    /// Creates a new force-transcode profile.
    /// </summary>
    [HttpPost("Profiles")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<ForceTranscodeProfile> CreateProfile([FromBody] ForceTranscodeProfile profile)
    {
        if (profile.UserId == Guid.Empty)
        {
            return BadRequest("UserId is required.");
        }

        profile.Id = Guid.NewGuid();

        var config = Plugin.Instance!.Configuration;
        config.Profiles.Add(profile);
        Plugin.Instance.SaveConfiguration();

        return CreatedAtAction(nameof(GetProfiles), profile);
    }

    /// <summary>
    /// Replaces an existing force-transcode profile.
    /// </summary>
    [HttpPut("Profiles/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdateProfile([FromRoute] Guid id, [FromBody] ForceTranscodeProfile profile)
    {
        var config = Plugin.Instance!.Configuration;
        var index = config.Profiles.FindIndex(p => p.Id == id);

        if (index < 0)
        {
            return NotFound();
        }

        profile.Id = id;
        config.Profiles[index] = profile;
        Plugin.Instance.SaveConfiguration();

        return NoContent();
    }

    /// <summary>
    /// Deletes a force-transcode profile.
    /// </summary>
    [HttpDelete("Profiles/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteProfile([FromRoute] Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        var removed = config.Profiles.RemoveAll(p => p.Id == id);

        if (removed == 0)
        {
            return NotFound();
        }

        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }
}
