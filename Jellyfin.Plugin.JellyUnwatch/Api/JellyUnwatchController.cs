using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.JellyUnwatch.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyUnwatch.Api;

[ApiController]
[Route("JellyUnwatch")]
[Authorize]
public class JellyUnwatchController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";
    private const string ScriptResource = "Jellyfin.Plugin.JellyUnwatch.Web.jelly-unwatch.js";

    private readonly HiddenItemStore _store;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<JellyUnwatchController> _logger;

    public JellyUnwatchController(
        HiddenItemStore store,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger<JellyUnwatchController> logger)
    {
        _store = store;
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    [HttpGet("ClientScript.js")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource);
        if (stream is null)
        {
            _logger.LogError("Embedded client script {Resource} was not found", ScriptResource);
            return NotFound();
        }

        Response.Headers.CacheControl = "no-cache";
        return File(stream, "application/javascript; charset=utf-8");
    }

    [HttpGet("ClientOptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ClientOptionsDto> GetClientOptions()
    {
        var configuration = Plugin.Instance?.Configuration;

        return new ClientOptionsDto
        {
            EnableWebButton = configuration?.EnableWebButton ?? true,
            ConfirmBeforeHiding = configuration?.ConfirmBeforeHiding ?? false,
            ClearResumePositionOnHide = configuration?.ClearResumePositionOnHide ?? false,
            AllowResetPlaybackProgress = configuration?.AllowResetPlaybackProgress ?? true,
            HideSeriesFromNextUp = configuration?.HideSeriesFromNextUp ?? true
        };
    }

    [HttpGet("Items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<HiddenItem>> GetHiddenItems()
    {
        var userId = GetUserId();
        if (userId.Equals(Guid.Empty))
        {
            return Unauthorized();
        }

        return Ok(_store.GetHidden(userId));
    }

    [HttpPost("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult HideItem(
        [FromRoute] Guid itemId,
        [FromQuery] bool? clearResume,
        [FromQuery] HideScope? scope)
    {
        var userId = GetUserId();
        if (userId.Equals(Guid.Empty))
        {
            return Unauthorized();
        }

        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var configuration = Plugin.Instance?.Configuration;
        var effectiveScope = scope ?? HideScope.Item;
        var seriesId = ItemSeries.GetSeriesId(item);

        _store.Hide(userId, itemId, seriesId, item.Name, effectiveScope, DescribeCaller());

        if (clearResume ?? configuration?.ClearResumePositionOnHide ?? false)
        {
            ClearResumePosition(userId, item);
        }

        return NoContent();
    }

    [HttpDelete("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult RestoreItem([FromRoute] Guid itemId)
    {
        var userId = GetUserId();
        if (userId.Equals(Guid.Empty))
        {
            return Unauthorized();
        }

        _store.Unhide(userId, itemId, DescribeCaller());
        return NoContent();
    }

    [HttpGet("Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<UserHiddenItemsDto>> GetAllHiddenItems()
    {
        var all = _store.GetAll();
        var result = new List<UserHiddenItemsDto>(all.Count);

        foreach (var pair in all)
        {
            result.Add(new UserHiddenItemsDto
            {
                UserId = pair.Key,
                UserName = _userManager.GetUserById(pair.Key)?.Username ?? pair.Key.ToString(),
                Items = pair.Value
            });
        }

        return Ok(result.OrderBy(x => x.UserName, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [HttpDelete("Users/{userId}/Items/{itemId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RestoreItemForUser([FromRoute] Guid userId, [FromRoute] Guid itemId)
    {
        _store.Unhide(userId, itemId, "admin page, " + DescribeCaller());
        return NoContent();
    }

    [HttpDelete("Users/{userId}/Items")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RestoreAllForUser([FromRoute] Guid userId)
    {
        _store.Clear(userId);
        return NoContent();
    }

    private string DescribeCaller()
    {
        var identity = HttpContext.User.Identity as ClaimsIdentity;
        var client = identity?.FindFirst("Jellyfin-Client")?.Value ?? "unknown client";
        var device = identity?.FindFirst("Jellyfin-Device")?.Value ?? "unknown device";

        return $"api request from {client} on {device}, {Request.Method} {Request.Path}";
    }

    private void ClearResumePosition(Guid userId, BaseItem item)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return;
        }

        var userData = _userDataManager.GetUserData(user, item);
        if (userData is null)
        {
            return;
        }

        userData.PlaybackPositionTicks = 0;
        _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.UpdateUserData, CancellationToken.None);
        _logger.LogInformation("Cleared resume position of {ItemId} for user {UserId}", item.Id, userId);
    }

    private Guid GetUserId()
    {
        if (HttpContext.User.Identity is not ClaimsIdentity identity)
        {
            return Guid.Empty;
        }

        var value = identity.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }
}

public class ClientOptionsDto
{
    public bool EnableWebButton { get; set; }

    public bool ConfirmBeforeHiding { get; set; }

    public bool ClearResumePositionOnHide { get; set; }

    public bool AllowResetPlaybackProgress { get; set; }

    public bool HideSeriesFromNextUp { get; set; }
}

public class UserHiddenItemsDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public IReadOnlyList<HiddenItem> Items { get; set; } = Array.Empty<HiddenItem>();
}
