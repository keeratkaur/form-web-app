using Microsoft.AspNetCore.Authorization;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using techNL_Forms_app.Data;
using techNL_Forms_app.Models;

namespace techNL_Forms_app.Controllers
{
    public class FormsController : Controller
    {
        private readonly FormManagementContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public FormsController(FormManagementContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        private async Task<string> SaveBannerImage(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
                return null;

            // Create uploads directory if it doesn't exist
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "banners");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            // Generate unique filename
            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(imageFile.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            // Save the file
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(fileStream);
            }

            // Return the relative path
            return $"/uploads/banners/{uniqueFileName}";
        }

        // MVC Actions
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10, string search = null)
        {
            var query = _context.Forms.Include(f => f.Fields).AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(f => f.Title.Contains(search) || f.Description.Contains(search));
            }
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var forms = await query
                .OrderByDescending(f => f.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Search = search;
            ViewBag.PageSize = pageSize;
            return View(forms);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Form form)
        {
            try
            {
                // Check if Title is provided
                if (string.IsNullOrWhiteSpace(form.Title))
                {
                    ModelState.AddModelError("Title", "Title is required");
                    return View(form);
                }

                // Handle banner image upload
                if (form.BannerImageFile != null)
                {
                    form.BannerImage = await SaveBannerImage(form.BannerImageFile);
                }

                // Set timestamps
                form.CreatedAt = DateTime.UtcNow;
                form.UpdatedAt = DateTime.UtcNow;

                // Generate unique link if not provided
                if (string.IsNullOrWhiteSpace(form.UniqueLink))
                {
                    form.UniqueLink = Guid.NewGuid().ToString("N");
                }

                // Initialize collections if they are null
                form.Fields ??= new List<FormField>();
                form.Submissions ??= new List<FormSubmission>();
                form.Analytics = new FormAnalytics
                {
                    TotalViews = 0,
                    UniqueViews = 0,
                    TotalSubmissions = 0,
                    LinkClicks = 0,
                    LastUpdated = DateTime.UtcNow
                };

                // Process form fields
                if (form.Fields != null && form.Fields.Any())
                {
                    foreach (var field in form.Fields)
                    {
                        // Ensure field has a reference to the form
                        field.FormId = form.FormId;
                        
                        // Process field options if they exist
                        if (field.Options != null)
                        {
                            field.Options = field.Options
                                .Where(o => !string.IsNullOrWhiteSpace(o.OptionLabel))
                                .Select((o, index) => { o.Order = index; return o; })
                                .ToList();
                        }
                    }
                }

                // Add and save the form
                _context.Forms.Add(form);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while creating the form: " + ex.Message);
                return View(form);
            }
        }

        public async Task<IActionResult> Edit(int id)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .FirstOrDefaultAsync(f => f.FormId == id);

            if (form == null)
            {
                return NotFound();
            }

            return View(form);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Form form)
        {
            if (id != form.FormId)
            {
                return NotFound();
            }

            try
            {
                // Handle banner image upload
                if (form.BannerImageFile != null)
                {
                    // Delete old banner image if exists
                    if (!string.IsNullOrEmpty(form.BannerImage))
                    {
                        var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, form.BannerImage.TrimStart('/'));
                        if (System.IO.File.Exists(oldImagePath))
                        {
                            System.IO.File.Delete(oldImagePath);
                        }
                    }

                    // Save new banner image
                    form.BannerImage = await SaveBannerImage(form.BannerImageFile);
                }

                // Get existing form to preserve fields and other data
                var existingForm = await _context.Forms
                    .Include(f => f.Fields)
                        .ThenInclude(ff => ff.Options)
                    .FirstOrDefaultAsync(f => f.FormId == id);

                if (existingForm == null)
                {
                    return NotFound();
                }

                // Update only the properties that should be updated
                existingForm.Title = form.Title;
                existingForm.Description = form.Description;
                existingForm.IsPublished = form.IsPublished;
                existingForm.StartDate = form.StartDate;
                existingForm.EndDate = form.EndDate;
                existingForm.UpdatedAt = DateTime.UtcNow;
                
                // Update banner image only if a new one was uploaded
                if (!string.IsNullOrEmpty(form.BannerImage))
                {
                    existingForm.BannerImage = form.BannerImage;
                }
                existingForm.BannerDescription = form.BannerDescription ?? string.Empty;

                _context.Update(existingForm);
                await _context.SaveChangesAsync();
                
                return RedirectToAction(nameof(Preview), new { id = form.FormId });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FormExists(form.FormId))
                {
                    return NotFound();
                }
                throw;
            }
            
            return View(form);
        }

        // API Endpoints
        [HttpGet("api/[controller]")]
        public async Task<ActionResult<IEnumerable<Form>>> GetForms()
        {
            var forms = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .ToListAsync();
            return Ok(forms);
        }

        // GET: api/Forms/{uniqueLink}
        [HttpGet("{uniqueLink}")]
        [AllowAnonymous]
        public async Task<ActionResult<Form>> GetForm(string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
            {
                return NotFound();
            }

            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .Include(f => f.Analytics)
                .FirstOrDefaultAsync(f =>
                    f.UniqueLink == uniqueLink &&
                    f.IsPublished &&
                    (f.StartDate == null || f.StartDate <= DateTime.UtcNow) &&
                    (f.EndDate == null || f.EndDate >= DateTime.UtcNow));

            if (form == null)
            {
                return NotFound();
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
                form.Analytics.LastUpdated = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            return Ok(form);
        }

        // POST: api/Forms
        [HttpPost]
        public async Task<ActionResult<Form>> CreateForm(Form form)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            form.CreatedAt = DateTime.UtcNow;
            form.UpdatedAt = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(form.UniqueLink))
            {
                form.UniqueLink = Guid.NewGuid().ToString("N");
            }
            else
            {
                form.UniqueLink = form.UniqueLink.Trim();
            }

            _context.Forms.Add(form);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetForm), new { uniqueLink = form.UniqueLink }, form);
        }

        // PUT: api/Forms/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateForm(int id, Form form)
        {
            if (id != form.FormId)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                form.UpdatedAt = DateTime.UtcNow;
                _context.Update(form);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FormExists(form.FormId))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Forms/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteForm(int id)
        {
            var form = await _context.Forms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            _context.Forms.Remove(form);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/Forms/{id}/publish
        [HttpPost("{id}/publish")]
        public async Task<IActionResult> PublishForm(int id)
        {
            var form = await _context.Forms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            form.IsPublished = true;
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/Forms/{id}/unpublish
        [HttpPost("{id}/unpublish")]
        public async Task<IActionResult> UnpublishForm(int id)
        {
            var form = await _context.Forms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            form.IsPublished = false;
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/Forms/{formId}/fields
        [HttpPost("{formId}/fields")]
        public async Task<ActionResult<FormField>> AddFormField(int formId, FormField field)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                .FirstOrDefaultAsync(f => f.FormId == formId);

            if (form == null)
            {
                return NotFound();
            }

            field.FormId = formId;

            if (field.Order <= 0)
            {
                field.Order = form.Fields.Any() ? form.Fields.Max(f => f.Order) + 1 : 1;
            }
            else if (form.Fields.Any(f => f.Order >= field.Order))
            {
                var fieldsToShift = form.Fields.Where(f => f.Order >= field.Order).ToList();
                foreach (var existingField in fieldsToShift)
                {
                    existingField.Order++;
                }
            }

            if (field.Options != null)
            {
                field.Options = field.Options
                    .Where(o => !string.IsNullOrWhiteSpace(o.OptionLabel))
                    .ToList();
            }

            form.Fields.Add(field);
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetForm), new { uniqueLink = form.UniqueLink }, field);
        }

        // PUT: api/Forms/fields/{id}
        [HttpPut("fields/{id}")]
        public async Task<IActionResult> UpdateField(int id, FormField field)
        {
            if (id != field.FieldId)
            {
                return BadRequest();
            }

            var existingField = await _context.FormFields
                .Include(f => f.Options)
                .FirstOrDefaultAsync(f => f.FieldId == id);

            if (existingField == null)
            {
                return NotFound();
            }

            existingField.Label = field.Label;
            existingField.FieldName = field.FieldName;
            existingField.Order = field.Order;
            existingField.IsRequired = field.IsRequired;

            if (existingField.FieldType.ToLower() is "dropdown" or "select" or "radio" or "checkbox" or "multiplechoice" or "checkboxes")
            {
                _context.FieldOptions.RemoveRange(existingField.Options);

                if (field.Options != null)
                {
                    var validOptions = field.Options
                        .Where(o => !string.IsNullOrWhiteSpace(o.OptionLabel))
                        .Select((o, index) => new FieldOption
                        {
                            FieldId = id,
                            OptionLabel = o.OptionLabel.Trim(),
                            Order = index
                        })
                        .ToList();

                    existingField.Options = validOptions;
                }
            }

            _context.Update(existingField);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/Forms/fields/{id}
        [HttpDelete("fields/{id}")]
        public async Task<IActionResult> DeleteField(int id)
        {
            var field = await _context.FormFields
                .Include(f => f.Options)
                .FirstOrDefaultAsync(f => f.FieldId == id);

            if (field == null)
            {
                return NotFound();
            }

            var formId = field.FormId;

            if (field.Options != null && field.Options.Any())
            {
                _context.FieldOptions.RemoveRange(field.Options);
            }

            _context.FormFields.Remove(field);

            var form = await _context.Forms.FindAsync(formId);
            if (form != null)
            {
                form.UpdatedAt = DateTime.UtcNow;
                _context.Update(form);
            }

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // POST: api/Forms/{formId}/reorder-fields
        [HttpPost("{formId}/reorder-fields")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReorderFields(int formId, [FromBody] List<int> fieldIds)
        {
            if (fieldIds == null || !fieldIds.Any())
            {
                return BadRequest("Field IDs are required");
            }

            var form = await _context.Forms
                .Include(f => f.Fields)
                .FirstOrDefaultAsync(f => f.FormId == formId);

            if (form == null)
            {
                return NotFound("Form not found");
            }

            // Update the order of each field
            for (int i = 0; i < fieldIds.Count; i++)
            {
                var field = form.Fields.FirstOrDefault(f => f.FieldId == fieldIds[i]);
                if (field != null)
                {
                    field.Order = i + 1;
                }
            }

            form.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok();
        }

        // MVC Actions for Form Fields
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveField(int id)
        {
            var field = await _context.FormFields
                .Include(f => f.Options)
                .FirstOrDefaultAsync(f => f.FieldId == id);

            if (field == null)
            {
                return NotFound();
            }

            var formId = field.FormId;

            // Remove any options associated with the field
            if (field.Options != null && field.Options.Any())
            {
                _context.FieldOptions.RemoveRange(field.Options);
            }

            _context.FormFields.Remove(field);

            // Update the form's UpdatedAt timestamp
            var form = await _context.Forms.FindAsync(formId);
            if (form != null)
            {
                form.UpdatedAt = DateTime.UtcNow;
                _context.Update(form);
            }

            await _context.SaveChangesAsync();

            // Redirect back to the Edit form page
            return RedirectToAction(nameof(Edit), new { id = formId });
        }

        // GET: Forms/AddField/{formId}
        public IActionResult AddField(int formId)
        {
            var form = _context.Forms.FirstOrDefault(f => f.FormId == formId);
            if (form == null)
            {
                return NotFound();
            }

            ViewBag.FormId = formId;
            return View(new FormField());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddField(int formId, FormField field, List<FieldOption> options)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                .FirstOrDefaultAsync(f => f.FormId == formId);
            if (form == null)
                return NotFound();

            field.FormId = formId;

            // Filter out any FieldOptions where OptionLabel is null or empty.
            if (options != null)
            {
                options = options
                    .Where(o => !string.IsNullOrWhiteSpace(o.OptionLabel))
                    .ToList();
            }
            field.Options = options ?? new List<FieldOption>();

            form.Fields.Add(field);
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = formId });
        }



        public async Task<IActionResult> EditField(int id)
        {
            var field = await _context.FormFields
                .Include(f => f.Options)
                .FirstOrDefaultAsync(f => f.FieldId == id);

            if (field == null)
            {
                return NotFound();
            }

            return View(field);
        }

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> EditField(int id, FormField field)
        //{
        //    if (id != field.FieldId)
        //    {
        //        return NotFound();
        //    }

        //    if (ModelState.IsValid)
        //    {
        //        try
        //        {
        //            _context.Update(field);
        //            await _context.SaveChangesAsync();
        //        }
        //        catch (DbUpdateConcurrencyException)
        //        {
        //            if (!FormFieldExists(field.FieldId))
        //            {
        //                return NotFound();
        //            }
        //            throw;
        //        }
        //        return RedirectToAction(nameof(Edit), new { id = field.FormId });
        //    }
        //    return View(field);
        //}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditField(int id, FormField field)
        {
            if (id != field.FieldId)
            {
                return NotFound();
            }

            //if (ModelState.IsValid)
            //{
                try
                {
                    // Load the existing field with its options from the database.
                    var existingField = await _context.FormFields
                        .Include(f => f.Options)
                        .FirstOrDefaultAsync(f => f.FieldId == field.FieldId);

                    if (existingField == null)
                    {
                        return NotFound();
                    }

                    // Update main field properties.
                    existingField.Label = field.Label;
                    existingField.FieldName = field.FieldName;
                    existingField.Order = field.Order;
                    existingField.IsRequired = field.IsRequired;
                    // Update additional properties if needed.

                    // Ensure posted options is not null.
                    var postedOptions = field.Options ?? new List<FieldOption>();

                    // Collect the OptionIds of options posted that already exist.
                    var postedOptionIds = postedOptions
                                            .Where(o => o.OptionId != 0)
                                            .Select(o => o.OptionId)
                                            .ToList();

                    // Remove options that exist in the database but weren't posted back.
                    var optionsToRemove = existingField.Options
                                            .Where(o => !postedOptionIds.Contains(o.OptionId))
                                            .ToList();
                    foreach (var option in optionsToRemove)
                    {
                        _context.FieldOptions.Remove(option);
                    }

                    // Process each posted option.
                    foreach (var postedOption in postedOptions)
                    {
                        if (postedOption.OptionId != 0)
                        {
                            // Update an existing option.
                            var existingOption = existingField.Options
                                                    .FirstOrDefault(o => o.OptionId == postedOption.OptionId);
                            if (existingOption != null)
                            {
                                existingOption.OptionLabel = postedOption.OptionLabel;
                                existingOption.Order = postedOption.Order;
                            }
                        }
                        else
                        {
                            // Add a new option.
                            var newOption = new FieldOption
                            {
                                
                                OptionLabel = postedOption.OptionLabel,
                                OptionValue = postedOption.OptionValue,
                                Order = postedOption.Order,
                                FieldId = existingField.FieldId
                            };
                            existingField.Options.Add(newOption);
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!FormFieldExists(field.FieldId))
                    {
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Edit), new { id = field.FormId });
            //}
            return View(field);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .Include(f => f.Submissions)
                    .ThenInclude(s => s.Values)
                .FirstOrDefaultAsync(f => f.FormId == id);

            if (form == null)
            {
                return NotFound();
            }

            // Delete all field options first
            foreach (var field in form.Fields)
            {
                if (field.Options != null && field.Options.Any())
                {
                    _context.FieldOptions.RemoveRange(field.Options);
                }
            }

            // Delete all form fields
            _context.FormFields.RemoveRange(form.Fields);

            // Delete all form submission values first
            foreach (var submission in form.Submissions)
            {
                if (submission.Values != null && submission.Values.Any())
                {
                    _context.FormSubmissionValues.RemoveRange(submission.Values);
                }
            }

            // Delete all form submissions
            if (form.Submissions != null && form.Submissions.Any())
            {
                _context.FormSubmissions.RemoveRange(form.Submissions);
            }

            // Finally delete the form
            _context.Forms.Remove(form);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            var form = await _context.Forms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            form.IsPublished = true;
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unpublish(int id)
        {
            var form = await _context.Forms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            form.IsPublished = false;
            form.UpdatedAt = DateTime.UtcNow;
            _context.Update(form);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool FormFieldExists(int id)
        {
            return _context.FormFields.Any(f => f.FieldId == id);
        }

        private bool FormExists(int id)
        {
            return _context.Forms.Any(f => f.FormId == id);
        }

        public async Task<IActionResult> Preview(int id)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(f => f.Options)
                .FirstOrDefaultAsync(f => f.FormId == id);

            if (form == null)
            {
                return NotFound();
            }

            // Redirect to the unique link version
            return RedirectToAction(nameof(PreviewByLink), new { uniqueLink = form.UniqueLink });
        }

        [HttpGet("Forms/p/{uniqueLink}")]
        public async Task<IActionResult> PreviewByLink(string uniqueLink)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(f => f.Options)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            return View("Preview", form);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateFieldOrders([FromBody] List<FieldOrderUpdate> updates)
        {
            try
            {
                foreach (var update in updates)
                {
                    var field = await _context.FormFields.FindAsync(update.fieldId);
                    if (field != null)
                    {
                        field.Order = update.order;
                    }
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class FieldOrderUpdate
        {
            public int fieldId { get; set; }
            public int order { get; set; }
        }

        [HttpGet("Forms/EditByLink/{uniqueLink}")]
        public async Task<IActionResult> EditByLink(string uniqueLink)
        {
            var form = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (form == null)
            {
                return NotFound();
            }

            return View("Edit", form);
        }

        [HttpPost("Forms/EditByLink/{uniqueLink}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditByLink(string uniqueLink, Form form)
        {
            var existingForm = await _context.Forms
                .Include(f => f.Fields)
                    .ThenInclude(ff => ff.Options)
                .FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);

            if (existingForm == null)
            {
                return NotFound();
            }

            try
            {
                // Handle banner image upload
                if (form.BannerImageFile != null)
                {
                    // Delete old banner image if exists
                    if (!string.IsNullOrEmpty(existingForm.BannerImage))
                    {
                        var oldImagePath = Path.Combine(_webHostEnvironment.WebRootPath, existingForm.BannerImage.TrimStart('/'));
                        if (System.IO.File.Exists(oldImagePath))
                        {
                            System.IO.File.Delete(oldImagePath);
                        }
                    }

                    // Save new banner image
                    form.BannerImage = await SaveBannerImage(form.BannerImageFile);
                }

                // Update only the properties that should be updated
                existingForm.Title = form.Title;
                existingForm.Description = form.Description;
                existingForm.IsPublished = form.IsPublished;
                existingForm.StartDate = form.StartDate;
                existingForm.EndDate = form.EndDate;
                existingForm.UpdatedAt = DateTime.UtcNow;
                
                // Update banner image only if a new one was uploaded
                if (!string.IsNullOrEmpty(form.BannerImage))
                {
                    existingForm.BannerImage = form.BannerImage;
                }
                existingForm.BannerDescription = form.BannerDescription ?? string.Empty;

                _context.Update(existingForm);
                await _context.SaveChangesAsync();
                
                return RedirectToAction(nameof(Preview), new { uniqueLink = existingForm.UniqueLink });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while updating the form: " + ex.Message);
                return View("Edit", form);
            }
        }

        [HttpGet("Forms/Preview")]
        public async Task<IActionResult> PreviewByQuery([FromQuery] string uniqueLink)
        {
            if (string.IsNullOrEmpty(uniqueLink))
                return NotFound();
            return await PreviewByLink(uniqueLink);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBannerImage(string uniqueLink)
        {
            var form = await _context.Forms.FirstOrDefaultAsync(f => f.UniqueLink == uniqueLink);
            if (form == null)
                return NotFound();

            if (!string.IsNullOrEmpty(form.BannerImage))
            {
                var filePath = Path.Combine(_webHostEnvironment.WebRootPath, form.BannerImage.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
                form.BannerImage = null;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TrackLinkClick([FromBody] TrackLinkClickModel model)
        {
            if (string.IsNullOrEmpty(model?.UniqueLink))
                return BadRequest();

            var form = await _context.Forms
                .Include(f => f.Analytics)
                .FirstOrDefaultAsync(f => f.UniqueLink == model.UniqueLink);
            if (form == null || form.Analytics == null)
                return NotFound();

            form.Analytics.LinkClicks++;
            form.Analytics.LastUpdated = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok();
        }

        public class TrackLinkClickModel
        {
            public string UniqueLink { get; set; }
        }
    }
}



