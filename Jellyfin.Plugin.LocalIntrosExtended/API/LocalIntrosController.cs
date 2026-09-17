using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using MediaBrowser.Common;
using MediaBrowser.Controller.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.LocalIntrosExtended.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalIntrosExtended;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("LocalIntrosExtended")]
[Produces(MediaTypeNames.Application.Json)]
public class LocalIntrosExtendedController : ControllerBase
{
    private readonly ILogger<LocalIntrosExtendedController> logger;
    private readonly IUserManager userManager;

    public LocalIntrosExtendedController(IApplicationHost appHost, ILoggerFactory loggerFactory, IUserManager userManager)
    {
        this.logger = loggerFactory.CreateLogger<LocalIntrosExtendedController>();
        this.userManager = userManager;
    }

    [HttpGet("SyncStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IntroSyncStatus> GetSyncStatus()
    {
        return Ok(IntroManager.GetSyncStatus(logger));
    }

    [HttpPost("RepairDatabase")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult RepairDatabase()
    {
        try
        {
            logger.LogInformation("[LocalIntrosExtended] Handmatig herstel van intro-database gestart.");
            var results = IntroManager.PopulateIntroLibrary(logger, preserveGuids: true);
            return Ok(new { success = true, count = results.Count, message = $"Database succesvol hersteld ({results.Count} video's)." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LocalIntrosExtended] Fout bij database-herstel");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("LoadIntros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult LoadIntros()
    {
        try
        {
            logger.LogDebug("[LocalIntrosExtended] LoadIntros aangeroepen.");
            var results = IntroManager.PopulateIntroLibrary(logger, preserveGuids: true);
            return Ok(new { success = true, count = results.Count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[LocalIntrosExtended] Fout bij laden van intro's");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("ClearIntros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ClearIntros()
    {
        logger.LogInformation("[LocalIntrosExtended] Wissen van alle intro-items uit Jellyfin-database.");
        var inLibrary = IntroManager.RetrieveIntroLibrary();
        foreach (var item in inLibrary.Values)
        {
            logger.LogInformation("[LocalIntrosExtended] Verwijderen van {Path} uit bibliotheek.", item.Path);
            LocalIntrosPlugin.LibraryManager.DeleteItem(item, new DeleteOptions());
        }
        return Ok();
    }
}
