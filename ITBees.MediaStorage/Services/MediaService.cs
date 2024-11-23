using ITBees.Interfaces.Platforms;
using ITBees.Interfaces.Repository;
using ITBees.MediaStorage.Controllers.Models;
using ITBees.MediaStorage.Interfaces;
using ITBees.Models.Media;
using ITBees.Models.Users;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.RestfulApiControllers.Models;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ITBees.MediaStorage.Services;

public class MediaService : IMediaService
{
    private readonly IAspCurrentUserService _aspCurrentUserService;
    private readonly IWriteOnlyRepository<MediaFile> _mediaFileRwRepo;
    private readonly IReadOnlyRepository<MediaFile> _mediaFileRoRepo;
    private readonly IPlatformSettingsService _platformSettingsService;
    private readonly ILogger<MediaService> _logger;

    public MediaService(IAspCurrentUserService aspCurrentUserService,
        IWriteOnlyRepository<MediaFile> mediaFileRwRepo,
        IReadOnlyRepository<MediaFile> mediaFileRoRepo,
        IPlatformSettingsService platformSettingsService,
        ILogger<MediaService> logger)
    {
        _aspCurrentUserService = aspCurrentUserService;
        _mediaFileRwRepo = mediaFileRwRepo;
        _mediaFileRoRepo = mediaFileRoRepo;
        _platformSettingsService = platformSettingsService;
        _logger = logger;
    }
    public async Task<UploadFileResultVm> UploadFile(IFormFile file, bool publicVisible,
        Guid? companyGuid)
    {
        try
        {
            var cu = _aspCurrentUserService.GetCurrentUserGuid();

            if (file == null || file.Length == 0)
                throw new FasApiErrorException(new FasApiErrorVm("File is not selected", 400, string.Empty));

            string rootFolder = _platformSettingsService.GetSetting("FtpRootFolderPath");
            string targetFolderPath = "";


            var path = string.Empty;
            if (path == "/") path = string.Empty;

            if (companyGuid == null)
            {
                if (publicVisible)
                {
                    var userFolder = Path.Combine(
                        rootFolder, cu.Value.ToString());
                    CreateFolderIfNotExists(userFolder);

                    targetFolderPath = Path.Combine(userFolder, "public");

                    CreateFolderIfNotExists(targetFolderPath);
                }
                else
                {
                    if (cu == null)
                    {
                        throw new UnauthorizedAccessException();
                    }

                    var userFolder = Path.Combine(
                        rootFolder, cu.Value.ToString());
                    CreateFolderIfNotExists(userFolder);
                    targetFolderPath = userFolder;
                }
            }
            else
            {
                if (publicVisible)
                {
                    var companyFolder = Path.Combine(
                        rootFolder, companyGuid.ToString());
                    CreateFolderIfNotExists(companyFolder);

                    targetFolderPath = Path.Combine(companyFolder, "public");

                    CreateFolderIfNotExists(targetFolderPath);
                }
                else
                {
                    var companyFolder = Path.Combine(
                        rootFolder, companyGuid.ToString());
                    CreateFolderIfNotExists(companyFolder);
                    targetFolderPath = companyFolder;
                }
            }

            var guid = Guid.NewGuid();

            var fileExtension = file.FileName.Split(".").Last();
            var targetFileName = $"{guid}.{fileExtension}";
            var newMediaFile = _mediaFileRwRepo.InsertData(new MediaFile()
            {
                CompanyGuid = companyGuid,
                Created = DateTime.Now,
                CreatedByGuid = cu.Value,
                FileExtension = fileExtension,
                OriginalFileName = file.FileName,
                FileName = targetFileName,
                FilePath = Path.Combine(targetFolderPath, targetFileName),
                FileSize = file.Length,
                Guid = guid,
                IsActive = true,
                PublicVisible = publicVisible,
                Type = ""
            });


            if (new FileInfo(newMediaFile.FilePath).Exists) throw new FasApiErrorException(new FasApiErrorVm("Plik o tej nazwie już istnieje", 400, ""));
            try
            {
                using (var stream = new FileStream(newMediaFile.FilePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            catch (Exception e)
            {
                _mediaFileRwRepo.DeleteData(x => x.Guid == guid);
            }


            return new UploadFileResultVm() { FileUrl = $"{_platformSettingsService.GetSetting("DefaultApiUrl")}/media?imageName={newMediaFile.FileName}", Size = file.Length, Success = true, PublicVisible = publicVisible };

        }
        catch (Exception e)
        {
            _logger.LogError(e.Message, e);
            throw;
        }
    }

    private static void CreateFolderIfNotExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public async Task<string> ResolveFilePath(string? resourceName, string? expectedFormat)
    {
        var fileGuid = resourceName.Split(".").First();
        var currentUserGuid = _aspCurrentUserService.GetCurrentUserGuid();
        var mediaFile = _mediaFileRoRepo.GetData(x => x.Guid == Guid.Parse(fileGuid)).FirstOrDefault();

        if (mediaFile == null)
        {
            throw new FasApiErrorException(new FasApiErrorVm("File not exists", 404, ""));
        }

        if (mediaFile is { PublicVisible: true, IsActive: true })
        {
            return mediaFile.FilePath;
        }

        if (_aspCurrentUserService.CurrentUserIsPlatformOperator())
        {
            return mediaFile.FilePath;
        }

        if (mediaFile.IsActive == false)
        {
            throw new FasApiErrorException(new FasApiErrorVm("File not exists", 404, ""));
        }

        if (mediaFile.PublicVisible == false && currentUserGuid == null)
        {
            throw new UnauthorizedAccessException();
        }

        if (mediaFile is { PublicVisible: false, CompanyGuid: not null })
        {
            _aspCurrentUserService.TryCanIDoForCompany(TypeOfOperation.Ro, mediaFile.CompanyGuid.Value);
            return mediaFile.FilePath;
        }
        if (mediaFile is { PublicVisible: false, CompanyGuid: null })
        {
            if (mediaFile.CreatedByGuid == currentUserGuid.Value)
                return mediaFile.FilePath;

            throw new UnauthorizedAccessException();
        }

        throw new FasApiErrorException(new FasApiErrorVm($"Could not get access to resource {resourceName}",400, string.Empty));
    }


}