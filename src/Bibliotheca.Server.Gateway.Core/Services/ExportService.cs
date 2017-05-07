using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Bibliotheca.Server.Gateway.Core.DataTransferObjects;
using Bibliotheca.Server.Gateway.Core.Exceptions;
using Bibliotheca.Server.Gateway.Core.HttpClients;

namespace Bibliotheca.Server.Gateway.Core.Services
{
    public class ExportService : IExportService
    {
        private readonly ITableOfContentsService _tableOfContentsService;

        private readonly IDocumentsService _documentsService;

        private readonly IProjectsService _projectsService;

        private readonly IPdfExportClient _pdfExportClient;

        public ExportService(
            ITableOfContentsService tableOfContentsService,
            IDocumentsService documentsService,
            IProjectsService projectsService,
            IPdfExportClient pdfExportClient)
        {
            _tableOfContentsService = tableOfContentsService;
            _documentsService = documentsService;
            _projectsService = projectsService;
            _pdfExportClient = pdfExportClient;
        }

        public async Task<byte[]> GeneratePdf(string projectId, string branchName)
        {
            var project = await _projectsService.GetProjectAsync(projectId);
            if(project == null) 
            {
                throw new ProjectNotFoundException($"Project '{projectId}' not exists.");
            }

            var chapters = await _tableOfContentsService.GetTableOfConents(projectId, branchName);
            

            var markdownBuilder = new StringBuilder();
            AddTitlePage(project, branchName, markdownBuilder);
            AddPageBreak(markdownBuilder);
            AddTableOfContents(chapters, markdownBuilder);
            AddPageBreak(markdownBuilder);
            await AddDocumentsContent(projectId, branchName, chapters, markdownBuilder);

            var markdown = markdownBuilder.ToString();
            var response = await _pdfExportClient.Post(markdown);

            if(response.IsSuccessStatusCode) 
            {
                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                return responseBytes;
            }

            var responseString = await response.Content.ReadAsStringAsync();
            throw new PdfExportException($"Exception during generating pdf. Status code: {response.StatusCode}. Message: {responseString}.");
        }

        private void AddTableOfContents(IList<ChapterItemDto> chapters, StringBuilder markdownBuilder)
        {
            markdownBuilder.AppendLine("<ul>");
            foreach (var item in chapters)
            {
                markdownBuilder.Append("<li>");
                
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    markdownBuilder.Append($"<span>{item.Name}</span>");
                }

                if(item.Children != null && item.Children.Count > 0)
                {
                    AddTableOfContents(item.Children, markdownBuilder);
                }

                markdownBuilder.Append("</li>");
            }
            markdownBuilder.AppendLine("</ul>");
        }

        private void AddTitlePage(ProjectDto project, string branchName, StringBuilder markdownBuilder)
        {
            var dateString = DateTime.Now.ToString("dd MMMM yyyy");
            var htmlVersion = $"<div style=\"text-align: right;\"><div>version: {branchName}</div><div>date: {dateString}</div></div>";
            var htmlTitle=  $"<div style=\"margin-top: 200px\"><center><h1>{project.Name}</h1></center></div>";

            var htmlDescriptiom = $"<div><div style=\"text-align: center; width: 240px;margin-top: 50px; margin-left: auto; margin-right: auto;\">{project.Description}</div></div>";

            markdownBuilder.AppendLine(htmlVersion);
            markdownBuilder.AppendLine(htmlTitle);
            markdownBuilder.AppendLine(htmlDescriptiom);
        }

        private void AddPageBreak(StringBuilder markdownBuilder)
        {
            markdownBuilder.AppendLine("<p style=\"page-break-after:always;\"></p>");
        }

        private async Task AddDocumentsContent(
            string projectId, 
            string branchName, 
            IList<DataTransferObjects.ChapterItemDto> chapters, 
            StringBuilder markdownBuilder)
        {
            foreach (var item in chapters)
            {
                if (!string.IsNullOrWhiteSpace(item.Url))
                {
                    var fileUri = item.Url.Replace("/", ":");
                    var document = await _documentsService.GetDocumentAsync(projectId, branchName, fileUri);
                    var markdown = Encoding.UTF8.GetString(document.Content);

                    markdownBuilder.Append(markdown);
                    markdownBuilder.AppendLine().AppendLine();
                }

                if(item.Children != null && item.Children.Count > 0)
                {
                    await AddDocumentsContent(projectId, branchName, item.Children, markdownBuilder);
                }
            }
        }
    }
}