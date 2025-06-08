using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using techNL_Forms_app.Data;
using techNL_Forms_app.Models;

namespace techNL_Forms_app.Controllers
{
    public class SubmissionsController : Controller
    {
        private readonly FormManagementContext _context;
        private readonly EmailHelper _emailHelper;
        private readonly IConfiguration _config;
        private readonly ILogger<SubmissionsController> _logger;

        public SubmissionsController(FormManagementContext context,
                                     EmailHelper emailHelper,
                                     IConfiguration config,
                                     ILogger<SubmissionsController> logger)
        {
            _context = context;
            _emailHelper = emailHelper;
            _config = config;
            _logger = logger;
        }
        // GET: /Submissions/Submit/{uniqueLink}
        // Display the form for public submission.
        [AllowAnonymous]
        public async Task<IActionResult> Submit(string uniqueLink)
        {
            try
            {
                if (string.IsNullOrEmpty(uniqueLink))
                {
                    _logger.LogWarning("Submit action called with null or empty uniqueLink");
                    return NotFound("Unique link is required.");
                }

                _logger.LogInformation($"Attempting to load form with uniqueLink: {uniqueLink}");

                var form = await _context.Forms
                    .Include(f => f.Fields)
                        .ThenInclude(ff => ff.Options)
                    .Include(f => f.Analytics)
                    .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

                if (form == null)
                {
                    _logger.LogWarning($"Form not found for uniqueLink: {uniqueLink}");
                    return NotFound("Form not found.");
                }

                _logger.LogInformation($"Form found. Title: {form.Title}, Fields count: {form.Fields?.Count ?? 0}");

                if (!form.IsPublished)
                {
                    _logger.LogWarning($"Form {form.FormId} is not published");
                    return NotFound("Form is not published.");
                }

                if (form.StartDate.HasValue && form.StartDate > DateTime.UtcNow)
                {
                    _logger.LogWarning($"Form {form.FormId} start date ({form.StartDate}) is in the future");
                    return NotFound($"Form will be available from {form.StartDate?.ToString("MMMM d, yyyy")}");
                }

                if (form.EndDate.HasValue && form.EndDate < DateTime.UtcNow)
                {
                    _logger.LogWarning($"Form {form.FormId} end date ({form.EndDate}) has passed");
                    return NotFound("Form submission period has ended.");
                }

                // Check if this is a unique view using a cookie
                bool isUniqueView = false;
                string cookieName = $"form_view_{form.FormId}";
                if (Request.Cookies[cookieName] == null)
                {
                    // Set a cookie that expires in 24 hours
                    Response.Cookies.Append(cookieName, "viewed", new CookieOptions
                    {
                        Expires = DateTime.Now.AddHours(24),
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax
                    });
                    isUniqueView = true;
                }

                // Track form view
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
                    if (isUniqueView)
                    {
                        form.Analytics.UniqueViews++;
                    }
                    form.Analytics.LastUpdated = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();

                return View(form);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading form with uniqueLink: {uniqueLink}");
                return StatusCode(500, "An error occurred while loading the form. Please try again later.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Submit(string uniqueLink, IFormCollection formCollection)
        {
            if (string.IsNullOrEmpty(uniqueLink))
                return NotFound();

            var form = await _context.Forms
                .Include(f => f.Fields)
                .Include(f => f.Analytics)
                .FirstOrDefaultAsync(f =>
                    f.UniqueLink == uniqueLink &&
                    f.IsPublished &&
                    (f.StartDate == null || f.StartDate <= DateTime.UtcNow) &&
                    (f.EndDate == null || f.EndDate >= DateTime.UtcNow));

            if (form == null)
                return NotFound();

            // Validate required fields
            foreach (var field in form.Fields.Where(f => f.IsRequired))
            {
                var fieldName = field.FieldType.ToLower() == "checkbox" || field.FieldType.ToLower() == "checkboxes" 
                    ? $"{field.FieldName}[]" 
                    : field.FieldName;

                if (!formCollection.ContainsKey(fieldName) || 
                    (field.FieldType.ToLower() == "checkbox" || field.FieldType.ToLower() == "checkboxes") 
                    ? !formCollection[fieldName].Any() 
                    : string.IsNullOrWhiteSpace(formCollection[fieldName].ToString()))
                {
                    ModelState.AddModelError(field.FieldName, $"{field.Label} is required");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(form);
            }

            // Capture the user's email from the form submission
            var submittedByEmail = formCollection["SubmittedBy"].ToString();

            var submission = new FormSubmission
            {
                FormId = form.FormId,
                SubmittedAt = DateTime.UtcNow,
                SubmittedBy = submittedByEmail
            };

            // Capture all field values
            foreach (var field in form.Fields)
            {
                try
                {
                    if (field.FieldType.ToLower() == "checkbox" || field.FieldType.ToLower() == "checkboxes")
                    {
                        // Get all selected values for this checkbox field
                        var fieldName = $"{field.FieldName}[]";
                        var selectedValues = new List<string>();

                        if (formCollection.ContainsKey(fieldName))
                        {
                            var values = formCollection[fieldName].ToArray();
                            if (values != null && values.Length > 0)
                            {
                                selectedValues.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)));
                            }
                        }

                        // Only add value if there are selected values or if field is not required
                        if (selectedValues.Any() || !field.IsRequired)
                        {
                            submission.Values.Add(new FormSubmissionValue
                            {
                                FieldId = field.FieldId,
                                Value = selectedValues.Any() ? string.Join(",", selectedValues) : null
                            });
                        }
                    }
                    else
                    {
                        var fieldName = field.FieldName;
                        if (formCollection.ContainsKey(fieldName))
                        {
                            var value = formCollection[fieldName].ToString();
                            // Only add value if it's not empty or if field is not required
                            if (!string.IsNullOrWhiteSpace(value) || !field.IsRequired)
                            {
                                submission.Values.Add(new FormSubmissionValue
                                {
                                    FieldId = field.FieldId,
                                    Value = value
                                });
                            }
                        }
                        else if (!field.IsRequired)
                        {
                            // Add null value for non-required fields that weren't submitted
                            submission.Values.Add(new FormSubmissionValue
                            {
                                FieldId = field.FieldId,
                                Value = null
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing field {field.FieldId} ({field.FieldName})");
                    // Continue processing other fields
                }
            }

            // Add submission to DB
            _context.FormSubmissions.Add(submission);

            // Update analytics
            if (form.Analytics == null)
            {
                form.Analytics = new FormAnalytics
                {
                    FormId = form.FormId,
                    TotalViews = 0,
                    UniqueViews = 0,
                    TotalSubmissions = 1,
                    LinkClicks = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _context.FormAnalytics.Add(form.Analytics);
            }
            else
            {
                form.Analytics.TotalSubmissions++;
                form.Analytics.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();


            // -----------------------------
            // Build the email body in HTML
            // -----------------------------
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='en'>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <meta charset='UTF-8'>");
            sb.AppendLine("  <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.AppendLine("  <title>New Form Submission</title>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body style='margin:0; padding:0; background-color:#f2f2f2;'>");
            sb.AppendLine("  <table border='0' cellpadding='0' cellspacing='0' width='100%'>");
            sb.AppendLine("    <tr>");
            sb.AppendLine("      <td align='center' style='padding:20px 0;'>");
            sb.AppendLine("        <!-- Outer container table -->");
            sb.AppendLine("        <table border='0' cellpadding='0' cellspacing='0' width='600' style='border:1px solid #ddd; border-radius:5px; overflow:hidden; background-color:#ffffff;'>");
            sb.AppendLine("          <!-- Email Header -->");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <td align='center' style='background-color:#333; padding:20px;'>");
            sb.AppendLine("              <h1 style='color:#ffffff; margin:0; font-family:Arial, sans-serif;'>New Form Submission Received</h1>");
            sb.AppendLine("            </td>");
            sb.AppendLine("          </tr>");

            sb.AppendLine("          <!-- Email Body -->");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <td style='padding:20px; font-family:Arial, sans-serif; color:#555555;'>");
            sb.AppendLine("              <p style='margin:0 0 16px; font-size:16px;'>A new form submission has been received. Please review the details below:</p>");
            sb.AppendLine("              <!-- Data Table -->");
            sb.AppendLine("              <table border='0' cellpadding='0' cellspacing='0' width='100%' style='border-collapse:collapse; margin-top:20px;'>");
            sb.AppendLine("                <tr>");
            sb.AppendLine("                  <th align='left' style='padding:8px 12px; background:#f9f9f9; border:1px solid #ddd; font-weight:bold;'>Field</th>");
            sb.AppendLine("                  <th align='left' style='padding:8px 12px; background:#f9f9f9; border:1px solid #ddd; font-weight:bold;'>Response</th>");
            sb.AppendLine("                </tr>");

            // Summarize each field in the submission
            foreach (var val in submission.Values)
            {
                var fieldLabel = form.Fields.FirstOrDefault(f => f.FieldId == val.FieldId)?.Label ?? val.FieldId.ToString();
                sb.AppendLine("                <tr>");
                sb.AppendLine($"                  <td style='padding:8px 12px; border:1px solid #ddd;'>{fieldLabel}</td>");
                sb.AppendLine($"                  <td style='padding:8px 12px; border:1px solid #ddd;'>{val.Value}</td>");
                sb.AppendLine("                </tr>");
            }

            sb.AppendLine("              </table>");
            sb.AppendLine("            </td>");
            sb.AppendLine("          </tr>");

            sb.AppendLine("          <!-- Email Footer -->");
            sb.AppendLine("          <tr>");
            sb.AppendLine("            <td align='center' style='background:#f9f9f9; padding:10px;'>");
            sb.AppendLine("              <p style='margin:0; font-size:12px; color:#999;'>");
            sb.AppendLine("                &copy; 2025 techNL. All rights reserved.");
            sb.AppendLine("              </p>");
            sb.AppendLine("            </td>");
            sb.AppendLine("          </tr>");

            sb.AppendLine("        </table>");
            sb.AppendLine("      </td>");
            sb.AppendLine("    </tr>");
            sb.AppendLine("  </table>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            // Prepare the final email body in HTML format
            var userEmailBody = sb.ToString();

            // --------------------------------
            // Send email to the user (submitter)
            // --------------------------------
            if (!string.IsNullOrEmpty(submittedByEmail))
            {
                await _emailHelper.SendEmailAsync(
                    submittedByEmail,
                    "Form Submission Confirmation",
                    userEmailBody
                    
                );
            }

            // -------------------------
            // Send email to the owner
            // -------------------------
            var ownerEmail = _config["ownerEmail"];
            if (!string.IsNullOrEmpty(ownerEmail))
            {
                // Optionally, create a slightly different email body for the owner
                var ownerEmailBody = $@"
    <!DOCTYPE html>
    <html lang='en'>
    <head>
      <meta charset='UTF-8'>
      <meta name='viewport' content='width=device-width, initial-scale=1.0'>
      <title>New Form Submission</title>
    </head>
    <body style='margin:0; padding:0; background-color:#f2f2f2;'>
      <table border='0' cellpadding='0' cellspacing='0' width='100%'>
        <tr>
          <td align='center' style='padding:20px 0;'>
            <table border='0' cellpadding='0' cellspacing='0' width='600' style='border:1px solid #ddd; border-radius:5px; overflow:hidden; background-color:#ffffff;'>
              <tr>
                <td align='center' style='background-color:#333; padding:20px;'>
                  <h1 style='color:#ffffff; margin:0; font-family:Arial, sans-serif;'>New Form Submission Received</h1>
                </td>
              </tr>
              <tr>
                <td style='padding:20px; font-family:Arial, sans-serif; color:#555555;'>
                  <p style='margin:0 0 16px; font-size:16px;'>A new form submission has been received. Below is the summary:</p>
                  {userEmailBody}
                </td>
              </tr>
              <tr>
                <td align='center' style='background:#f9f9f9; padding:10px;'>
                  <p style='margin:0; font-size:12px; color:#999;'>&copy; 2025 Your Company. All rights reserved.</p>
                </td>
              </tr>
            </table>
          </td>
        </tr>
      </table>
    </body>
    </html>
    ";

                await _emailHelper.SendEmailAsync(
                    ownerEmail,
                    $"New Form Submission: {form.Title}",
                    ownerEmailBody
                   
                );
            }


            return RedirectToAction("ThankYou");
        }




        // GET: /Submissions/ThankYou
        [AllowAnonymous]
        public IActionResult ThankYou()
        {
            return View();
        }
    }
}
