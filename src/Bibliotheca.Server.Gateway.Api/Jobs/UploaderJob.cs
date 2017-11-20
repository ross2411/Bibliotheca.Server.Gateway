using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bibliotheca.Server.Gateway.Core.Parameters;
using Bibliotheca.Server.Gateway.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Server.Gateway.Api.Jobs
{
    /// <summary>
    /// Job for uploading new documents.
    /// </summary>
    public class UploaderJob : IUploaderJob
    {
        private readonly IDocumentsService _documentsService;

        private readonly IBranchesService _branchService;

        private readonly ISearchService _searchService;

        private readonly IProjectsService _projectsService;

        private readonly ILogger<UploaderJob> _logger;

        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly ApplicationParameters _applicationParameters;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="documentsService">Documents service.</param>
        /// <param name="branchService">Branch service.</param>
        /// <param name="searchService">Search service.</param>
        /// <param name="projectsService">Projects service.</param>
        /// <param name="logger">Logger service.</param>
        /// <param name="httpContextAccessor">Http context service.</param>
        /// <param name="applicationParameters">Application parameters.</param>
        public UploaderJob(
            IDocumentsService documentsService, 
            IBranchesService branchService,
            ISearchService searchService,
            IProjectsService projectsService,
            ILogger<UploaderJob> logger,
            IHttpContextAccessor httpContextAccessor,
            IOptions<ApplicationParameters> applicationParameters)
        {
            _documentsService = documentsService;
            _branchService = branchService;
            _searchService = searchService;
            _projectsService = projectsService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _applicationParameters = applicationParameters.Value;
        }

        /// <summary>
        /// Upload new documents to the application.
        /// </summary>
        /// <param name="projectId">Project Id.</param>
        /// <param name="branchName">Branch name.</param>
        /// <param name="filePath">File path.</param>
        /// <returns>Returns async task.</returns>
        public async Task UploadBranchAsync(string projectId, string branchName, string filePath)
        {
            try
            {
                _httpContextAccessor.HttpContext.Request.Headers.Add("Authorization", $"SecureToken {_applicationParameters.SecureToken}");

                _logger.LogInformation($"[Uploading] Getting branch information ({projectId}/{branchName}).");
                var branches = await _branchService.GetBranchesAsync(projectId);

                if(branches.Any(x => x.Name == branchName))
                {
                    _logger.LogInformation($"[Uploading] Deleting branch from storage ({projectId}/{branchName}).");
                    await _branchService.DeleteBranchAsync(projectId, branchName);
                    _logger.LogInformation($"[Uploading] Branch deleted ({projectId}/{branchName}).");
                }
                
                _logger.LogInformation($"[Uploading] Upload branch to storage ({projectId}/{branchName}).");
                using(FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    await _documentsService.UploadBranchAsync(projectId, branchName, fileStream);
                }
                _logger.LogInformation($"[Uploading] Branch uploaded ({projectId}/{branchName}).");

                _logger.LogInformation($"[Uploading] Getting project information ({projectId}/{branchName}).");
                var project = await _projectsService.GetProjectAsync(projectId);

                if(!project.IsAccessLimited) 
                {
                    _logger.LogInformation($"[Uploading] Ordering index refresh ({projectId}/{branchName}).");
                    await _searchService.RefreshIndexAsync(projectId, branchName);
                    _logger.LogInformation($"[Uploading] Index refresh ordered ({projectId}/{branchName}).");
                }
            }
            catch(Exception exception)
            {
                _logger.LogError($"[Uploading] During uploadind exception occurs ({projectId}/{branchName}).");
                _logger.LogError($"[Uploading] Exception: {exception.ToString()}.");
                _logger.LogError($"[Uploading] Message: {exception.Message}.");
                _logger.LogError($"[Uploading] Stack trace: {exception.StackTrace}.");

                if(exception.InnerException != null)
                {
                    _logger.LogError($"[Uploading] Inner exception: {exception.InnerException.ToString()}.");
                    _logger.LogError($"[Uploading] Inner exception message: {exception.InnerException.Message}.");
                    _logger.LogError($"[Uploading] Inner exception stack trace: {exception.InnerException.StackTrace}.");
                }
                
            }
            finally
            {
                File.Delete(filePath);
            }
        }
    }
}