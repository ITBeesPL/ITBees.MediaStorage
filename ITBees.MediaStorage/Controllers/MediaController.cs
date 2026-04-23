using ITBees.MediaStorage.Controllers.Models;
using ITBees.MediaStorage.Interfaces;
using ITBees.RestfulApiControllers;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.RestfulApiControllers.Models;
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
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return NotFound(new FasApiErrorVm($"File not exists : {imageName}", 404, ""));
        }

        try
        {
            var extension = imageName.Split(".").Last();
            var resourcePath = await _mediaService.ResolveFilePath(imageName, extension);
            return PhysicalFile(resourcePath, "image/jpeg"); //fix for other types
        }
        catch (FasApiErrorException fasEx)
        {
            if (fasEx.FasApiErrorVm.StatusCode < 500)
            {
                _logger.LogInformation(
                    "MediaController returning {statusCode} for imageName={imageName}: {message}",
                    fasEx.FasApiErrorVm.StatusCode, imageName, fasEx.Message);
            }
            else
            {
                _logger.LogError(fasEx, "MediaController unexpected error for imageName={imageName}: {message}", imageName, fasEx.Message);
            }
            return StatusCode(fasEx.FasApiErrorVm.StatusCode, fasEx.FasApiErrorVm);
        }
        catch (UnauthorizedAccessException uex)
        {
            _logger.LogWarning("MediaController unauthorized access for imageName={imageName}: {message}", imageName, uex.Message);
            return Unauthorized(new FasApiErrorVm(uex.Message, 401, ""));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "MediaController error for imageName={imageName}: {message}", imageName, e.Message);
            return StatusCode(500, new FasApiErrorVm(e.Message, 500, ""));
        }
    }
}