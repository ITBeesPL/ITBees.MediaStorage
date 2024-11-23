using ITBees.MediaStorage.Controllers.Models;
using ITBees.MediaStorage.Interfaces;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.MediaStorage.Controllers;

public class MediaController : RestfulControllerBase<MediaController>
{
    private readonly ILogger<MediaController> _logger;
    private readonly IMediaService _mediaService;

    public MediaController(ILogger<MediaController> logger, IMediaService mediaService) : base(logger)
    {
        _logger = logger;
        _mediaService = mediaService;
    }

    [HttpPost]
    [Produces<UploadFileResultVm>]
    public async Task<IActionResult> UploadFile(IFormFile file, [FromForm] bool publicVisible, [FromForm] Guid? companyGuid)
    {
        return await ReturnOkResultAsync(async () => await _mediaService.UploadFile(file, publicVisible, companyGuid));
    }
    
    [HttpGet]
    public async Task<IActionResult> Get(string? imageName, int maxResolutionWith = 0, int maxResolutionHeight = 0)
    {
        try
        {
            var extension = imageName.Split(".").Last();
            var resourcePath = await _mediaService.ResolveFilePath(imageName, extension);
            return PhysicalFile(resourcePath, "image/jpeg"); //fix for other types
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message, e);
            return CreateBaseErrorResponse(e.Message, new { imageName });
        }
    }
}