using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using techNL_Forms_app.Data;
using techNL_Forms_app.Models;
using ClosedXML.Excel;
using System.IO;

namespace techNL_Forms_app.Controllers
{
    public class AnalyticsController : Controller
    {
        private readonly FormManagementContext _context;

        public AnalyticsController(FormManagementContext context)
        {
            _context = context;
        }

        // GET: Analytics
        [Route("[controller]")]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 8, string search = null)
        {
            var query = _context.Forms
                .Include(f => f.Analytics)
                .Include(f => f.Submissions)
                .OrderByDescending(f => f.UpdatedAt)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(f => f.Title.Contains(search) || f.Description.Contains(search));
            }
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var forms = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Search = search;
            ViewBag.PageSize = pageSize;
            return View(forms);
        }

        // GET: Analytics/Details/{uniqueLink}
        [Route("[controller]/Details/{uniqueLink}")]
        public async Task<IActionResult> Details(string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
            {
                return NotFound();
            }

            var form = await _context.Forms
                .Include(f => f.Analytics)
                .Include(f => f.Submissions)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            // Initialize analytics if it doesn't exist
            if (form.Analytics == null)
            {
                form.Analytics = new FormAnalytics
                {
                    FormId = form.FormId,
                    TotalViews = 0,
                    UniqueViews = 0,
                    TotalSubmissions = form.Submissions?.Count ?? 0,
                    LinkClicks = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _context.FormAnalytics.Add(form.Analytics);
                await _context.SaveChangesAsync();
            }

            return View(form);
        }

        // POST: Analytics/IncrementViews/{uniqueLink}
        [HttpPost]
        public async Task<IActionResult> IncrementViews(string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
            {
                return NotFound();
            }

            var form = await _context.Forms
                .Include(f => f.Analytics)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            if (form.Analytics == null)
            {
                form.Analytics = new FormAnalytics
                {
                    FormId = form.FormId,
                    TotalViews = 1,
                    UniqueViews = 1,
                    TotalSubmissions = 0,
                    LinkClicks = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _context.FormAnalytics.Add(form.Analytics);
            }
            else
            {
                form.Analytics.TotalViews++;
                form.Analytics.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        // POST: Analytics/IncrementLinkClicks/{uniqueLink}
        [HttpPost]
        public async Task<IActionResult> IncrementLinkClicks(string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
            {
                return NotFound();
            }

            var form = await _context.Forms
                .Include(f => f.Analytics)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            if (form.Analytics == null)
            {
                form.Analytics = new FormAnalytics
                {
                    FormId = form.FormId,
                    TotalViews = 0,
                    UniqueViews = 0,
                    TotalSubmissions = 0,
                    LinkClicks = 1,
                    LastUpdated = DateTime.UtcNow
                };
                _context.FormAnalytics.Add(form.Analytics);
            }
            else
            {
                form.Analytics.LinkClicks++;
                form.Analytics.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        // GET: Analytics/ExportSubmissions/{uniqueLink}
        [Route("[controller]/ExportSubmissions/{uniqueLink}")]
        public async Task<IActionResult> ExportSubmissions(string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
            {
                return NotFound();
            }

            var form = await _context.Forms
                .Include(f => f.Fields)
                .Include(f => f.Submissions)
                    .ThenInclude(s => s.Values)
                        .ThenInclude(v => v.Field)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Submissions");

                // Add headers
                worksheet.Cell(1, 1).Value = "Submission ID";
                worksheet.Cell(1, 2).Value = "Submitted By";
                worksheet.Cell(1, 3).Value = "Submitted At";

                int columnIndex = 4;
                var fieldColumns = new Dictionary<int, int>(); // FieldId to Column mapping
                foreach (var field in form.Fields.OrderBy(f => f.Order))
                {
                    worksheet.Cell(1, columnIndex).Value = field.Label;
                    fieldColumns[field.FieldId] = columnIndex;
                    columnIndex++;
                }

                // Style the header row
                var headerRow = worksheet.Row(1);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

                // Add data
                int rowIndex = 2;
                foreach (var submission in form.Submissions.OrderByDescending(s => s.SubmittedAt))
                {
                    worksheet.Cell(rowIndex, 1).Value = submission.SubmissionId;
                    worksheet.Cell(rowIndex, 2).Value = submission.SubmittedBy;
                    worksheet.Cell(rowIndex, 3).Value = submission.SubmittedAt.ToString("yyyy-MM-dd HH:mm:ss");

                    foreach (var value in submission.Values)
                    {
                        if (fieldColumns.ContainsKey(value.FieldId))
                        {
                            worksheet.Cell(rowIndex, fieldColumns[value.FieldId]).Value = value.Value;
                        }
                    }

                    rowIndex++;
                }

                // Auto-fit columns
                worksheet.Columns().AdjustToContents();

                // Generate the file
                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    string fileName = $"{form.Title}_Submissions_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }
    }
}
