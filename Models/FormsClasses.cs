using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Forms_app.Models
{
    public class Form
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FormId { get; set; }

        [Required(ErrorMessage = "Title is required")]
        public string Title { get; set; }
        public string? Description { get; set; }
        public string? BannerImage { get; set; }
        public string? BannerDescription { get; set; }

        [NotMapped]
        public IFormFile? BannerImageFile { get; set; }
        
        public int? OwnerId { get; set; }
        public bool IsPublished { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Unique link (slug) for sharing the form publicly.
        public string UniqueLink { get; set; }

        // Navigation properties
        public virtual ICollection<FormField> Fields { get; set; }
        public virtual ICollection<FormSubmission> Submissions { get; set; }
        public virtual FormAnalytics Analytics { get; set; }

        public Form()
        {
            Fields = new List<FormField>();
            Submissions = new List<FormSubmission>();
            Analytics = new FormAnalytics
            {
                TotalViews = 0,
                UniqueViews = 0,
                TotalSubmissions = 0,
                LinkClicks = 0,
                LastUpdated = DateTime.UtcNow
            };
        }
    }
    public class FormField
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FieldId { get; set; }

        // Foreign key to Form
        public int FormId { get; set; }

        [Required(ErrorMessage = "Field Type is required")]
        public string FieldType { get; set; } // e.g., "Text", "Dropdown", etc.

        [Required(ErrorMessage = "Label is required")]
        public string Label { get; set; }

        [Required(ErrorMessage = "Field Name is required")]
        public string FieldName { get; set; }

        public bool IsRequired { get; set; }
        
        [Required]
        public int Order { get; set; }

        // Normalized field options stored in the database.
        public ICollection<FieldOption> Options { get; set; } = new List<FieldOption>();

        // Navigation property to the parent Form.
        [ForeignKey("FormId")]
        public Form Form { get; set; }
    }
    public class FieldOption
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int OptionId { get; set; }

        // Foreign key to FormField
        public int FieldId { get; set; }

        public string OptionValue { get; set; }
        public string OptionLabel { get; set; }
        public int Order { get; set; }

        // Navigation property back to the FormField.
        [ForeignKey("FieldId")]
        public FormField Field { get; set; }
    }
    public class FormSubmission
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SubmissionId { get; set; }

        // Foreign key to Form
        public int FormId { get; set; }

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public string SubmittedBy { get; set; } // e.g., email or user identifier

        // Navigation property to the Form.
        [ForeignKey("FormId")]
        public Form Form { get; set; }

        public ICollection<FormSubmissionValue> Values { get; set; } = new List<FormSubmissionValue>();
    }

    public class FormSubmissionValue
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        // Foreign key to FormSubmission
        public int SubmissionId { get; set; }

        // Foreign key to FormField
        public int FieldId { get; set; }

        public string Value { get; set; }

        // Navigation properties.
        [ForeignKey("SubmissionId")]
        public FormSubmission Submission { get; set; }

        [ForeignKey("FieldId")]
        public FormField Field { get; set; }
    }

    public class FormAnalytics
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AnalyticsId { get; set; }

        // Foreign key to Form
        public int FormId { get; set; }

        public int TotalViews { get; set; }
        public int UniqueViews { get; set; }
        public int TotalSubmissions { get; set; }
        public int LinkClicks { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation property to the Form
        [ForeignKey("FormId")]
        public Form Form { get; set; }
    }
}

