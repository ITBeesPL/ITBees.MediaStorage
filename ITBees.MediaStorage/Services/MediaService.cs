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

namespace ITBees.MediaStorage.Services
{
    public class MediaService : IMediaService
    {
        private readonly IAspCurrentUserService _aspCurrentUserService;
        private readonly IWriteOnlyRepository<MediaFile> _mediaFileRwRepo;
        private readonly IReadOnlyRepository<MediaFile> _mediaFileRoRepo;
        private readonly IPlatformSettingsService _platformSettingsService;
        private readonly ILogger<MediaService> _logger;

        public MediaService(
            IAspCurrentUserService aspCurrentUserService,
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

        public async Task<UploadFileResultVm> UploadFile(IFormFile file, bool publicVisible, Guid? companyGuid)
        {
            return await UploadFile<MediaFile>(file, publicVisible, companyGuid, null);
        }

        public async Task<UploadFileResultVm> UploadFile<T>(IFormFile file, bool publicVisible, Guid? companyGuid, T mediaFile) where T : MediaFile, new()
        {
            try
            {
                var cu = _aspCurrentUserService.GetCurrentUserGuid();

                if (file == null || file.Length == 0)
                    throw new FasApiErrorException(new FasApiErrorVm("File is not selected", 400, string.Empty));

                string rootFolder = _platformSettingsService.GetSetting("FtpRootFolderPath");
                string targetFolderPath = BuildTargetFolderPath(rootFolder, publicVisible, cu, companyGuid);

                var guid = Guid.NewGuid();
                var fileExtension = GetFileExtension(file.FileName);
                var targetFileName = $"{guid}.{fileExtension}";
                var filePath = Path.Combine(targetFolderPath, targetFileName);
                if (mediaFile == null)
                {
                    mediaFile = new T()
                    {
                        CompanyGuid = companyGuid,
                        Created = DateTime.Now,
                        CreatedByGuid = cu ?? Guid.Empty,
                        FileExtension = fileExtension,
                        OriginalFileName = file.FileName,
                        FileName = targetFileName,
                        FilePath = filePath,
                        FileSize = file.Length,
                        Guid = guid,
                        IsActive = true,
                        PublicVisible = publicVisible,
                        Type = ""
                    };    
                }
                else
                {
                    mediaFile.CompanyGuid = companyGuid;
                    mediaFile.Created = DateTime.Now;
                    mediaFile.CreatedByGuid = cu ?? Guid.Empty;
                    mediaFile.FileExtension = fileExtension;
                    mediaFile.OriginalFileName = file.FileName;
                    mediaFile.FileName = targetFileName;
                    mediaFile.FilePath = filePath;
                    mediaFile.FileSize = file.Length;
                    mediaFile.Guid = guid;
                    mediaFile.IsActive = true;
                    mediaFile.PublicVisible = publicVisible;
                    mediaFile.Type = "";
                }
                
                var newMediaFile = _mediaFileRwRepo.InsertData(mediaFile);

                if (new FileInfo(newMediaFile.FilePath).Exists)
                    throw new FasApiErrorException(new FasApiErrorVm("File with this name already exists", 400, ""));

                try
                {
                    CreateFolderIfNotExists(targetFolderPath);
                    using (var stream = new FileStream(newMediaFile.FilePath, FileMode.Create, FileAccess.Write,
                               FileShare.None))
                    {
                        await file.CopyToAsync(stream);
                    }
                }
                catch
                {
                    _mediaFileRwRepo.DeleteData(x => x.Guid == guid);
                    throw;
                }

                return new UploadFileResultVm()
                {
                    FileUrl =
                        $"{_platformSettingsService.GetSetting("DefaultApiUrl")}/media?imageName={newMediaFile.FileName}",
                    Size = file.Length,
                    Success = true,
                    PublicVisible = publicVisible
                };
            }
            catch (Exception e)
            {
                _logger.LogError("Media service error : " + e.Message, e);
                _logger.LogError("Serialized error: {SerializedError}", System.Text.Json.JsonSerializer.Serialize(mediaFile));
                throw;
            }
        }

        public async Task<UploadFileResultVm> SaveFromStreamAsync(Guid? companyGuid, Stream inputStream,
            string originalFileName, CancellationToken ct, bool publicVisible = false)
        {
            try
            {
                if (inputStream == null || !inputStream.CanRead)
                    throw new FasApiErrorException(new FasApiErrorVm("No input data, or stream broken", 400,
                        string.Empty));

                var cu = _aspCurrentUserService.GetCurrentUserGuid();
                string rootFolder = _platformSettingsService.GetSetting("FtpRootFolderPath");

                string targetFolderPath = BuildTargetFolderPath(rootFolder, publicVisible, cu, companyGuid);

                var guid = Guid.NewGuid();
                var fileExtension = GetFileExtension(originalFileName);
                var targetFileName = $"{guid}.{fileExtension}";
                var filePath = Path.Combine(targetFolderPath, targetFileName);

                long totalWrittenBytes = 0;

                var newMediaFile = _mediaFileRwRepo.InsertData(new MediaFile()
                {
                    CompanyGuid = companyGuid,
                    Created = DateTime.Now,
                    CreatedByGuid = cu ?? null,
                    FileExtension = fileExtension,
                    OriginalFileName = originalFileName,
                    FileName = targetFileName,
                    FilePath = filePath,
                    FileSize = 0, // this will be updated after save
                    Guid = guid,
                    IsActive = true,
                    PublicVisible = publicVisible,
                    Type = ""
                });

                if (new FileInfo(newMediaFile.FilePath).Exists)
                    throw new FasApiErrorException(new FasApiErrorVm("File with this name already exist", 400, ""));

                try
                {
                    CreateFolderIfNotExists(targetFolderPath);

                    if (inputStream.CanSeek)
                        inputStream.Seek(0, SeekOrigin.Begin);

                    var buffer = new byte[1024 * 64];
                    using (var fs = new FileStream(newMediaFile.FilePath, FileMode.CreateNew, FileAccess.Write,
                               FileShare.None, bufferSize: buffer.Length, useAsync: true))
                    {
                        int read;
                        while ((read = await inputStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                            totalWrittenBytes += read;
                        }

                        await fs.FlushAsync(ct);
                    }

                    newMediaFile.FileSize = totalWrittenBytes;
                    _mediaFileRwRepo.UpdateData(x => x.Guid == guid, mf => mf.FileSize = totalWrittenBytes);
                }
                catch
                {
                    _mediaFileRwRepo.DeleteData(x => x.Guid == guid);
                    TryDeleteFileSafe(filePath);
                    throw;
                }

                return new UploadFileResultVm()
                {
                    FileUrl =
                        $"{_platformSettingsService.GetSetting("DefaultApiUrl")}/media?imageName={targetFileName}",
                    Size = totalWrittenBytes,
                    Success = true,
                    PublicVisible = publicVisible
                };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Operation canceled");
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }

        public async Task DeleteFile(string fullImageUrlPath, bool noCheckingPermissions = false)
        {
            try
            {
                var fileGuid = fullImageUrlPath.Split("/media?imageName=").Last().Split(".").First();

                var fileMeta = _mediaFileRoRepo.GetData(x => x.Guid == new Guid(fileGuid)).FirstOrDefault() ??
                               throw new FasApiErrorException(new FasApiErrorVm("File not exists", 404, ""));

                if (noCheckingPermissions)
                {
                    _mediaFileRwRepo.DeleteData(x => x.Guid == new Guid(fileGuid));
                    File.Delete(fileMeta.FilePath);
                }
                else
                {
                    //todo permission check https://monito.atlassian.net/browse/OC-455
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw new Exception($"Could not delete file - {fullImageUrlPath}" + e.Message);
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

            throw new FasApiErrorException(new FasApiErrorVm($"Could not get access to resource {resourceName}", 400,
                string.Empty));
        }

        private static string BuildTargetFolderPath(string rootFolder, bool publicVisible, Guid? currentUserGuid,
            Guid? companyGuid)
        {
            string targetFolderPath;

            if (companyGuid != null)
            {
                var companyFolder = Path.Combine(rootFolder, companyGuid.ToString());
                CreateFolderIfNotExists(companyFolder);

                targetFolderPath = publicVisible
                    ? Path.Combine(companyFolder, "public")
                    : companyFolder;

                CreateFolderIfNotExists(targetFolderPath);
            }
            else
            {
                if (currentUserGuid == null)
                    throw new UnauthorizedAccessException();

                var userFolder = Path.Combine(rootFolder, currentUserGuid.Value.ToString());
                CreateFolderIfNotExists(userFolder);

                targetFolderPath = publicVisible
                    ? Path.Combine(userFolder, "public")
                    : userFolder;

                CreateFolderIfNotExists(targetFolderPath);
            }

            return targetFolderPath;
        }

        private static string GetFileExtension(string fileNameOrPath)
        {
            var parts = fileNameOrPath?.Split('.');
            if (parts == null || parts.Length < 2)
                return "bin"; //temporary extension

            var ext = parts.Last();
            return string.IsNullOrWhiteSpace(ext) ? "bin" : ext.Trim().ToLowerInvariant();
        }

        private static void TryDeleteFileSafe(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                Console.WriteLine("Could not delete file: " + path);
            }
        }

        private static void CreateFolderIfNotExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}