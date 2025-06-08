using Microsoft.EntityFrameworkCore;
using Forms_app.Models;

namespace Forms_app.Data
{
    public class FormManagementContext : DbContext
    {
        public FormManagementContext(DbContextOptions<FormManagementContext> options)
            : base(options) { }

        public DbSet<Form> Forms { get; set; }
        public DbSet<FormField> FormFields { get; set; }
        public DbSet<FieldOption> FieldOptions { get; set; }
        public DbSet<FormSubmission> FormSubmissions { get; set; }
        public DbSet<FormSubmissionValue> FormSubmissionValues { get; set; }
        public DbSet<FormAnalytics> FormAnalytics { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationship between Form and FormField
            modelBuilder.Entity<Form>()
                .HasMany(f => f.Fields)
                .WithOne(ff => ff.Form)
                .HasForeignKey(ff => ff.FormId)
                .OnDelete(DeleteBehavior.NoAction);

            // Configure relationship between FormField and FieldOption
            modelBuilder.Entity<FormField>()
                .HasMany(ff => ff.Options)
                .WithOne(o => o.Field)
                .HasForeignKey(o => o.FieldId)
                .OnDelete(DeleteBehavior.NoAction);

            // Configure relationship between Form and FormSubmission
            modelBuilder.Entity<Form>()
                .HasMany(f => f.Submissions)
                .WithOne(s => s.Form)
                .HasForeignKey(s => s.FormId)
                .OnDelete(DeleteBehavior.NoAction);

            // Configure relationship between FormSubmission and FormSubmissionValue
            modelBuilder.Entity<FormSubmission>()
                .HasMany(s => s.Values)
                .WithOne(v => v.Submission)
                .HasForeignKey(v => v.SubmissionId)
                .OnDelete(DeleteBehavior.NoAction);

            // Configure relationship between Form and FormAnalytics
            modelBuilder.Entity<Form>()
                .HasOne(f => f.Analytics)
                .WithOne(a => a.Form)
                .HasForeignKey<FormAnalytics>(a => a.FormId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
