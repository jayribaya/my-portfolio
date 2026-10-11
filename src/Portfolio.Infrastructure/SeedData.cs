using Portfolio.Application;
using Portfolio.Domain;
namespace Portfolio.Infrastructure;
public static class SeedData
{
    public static async Task Seed(IPortfolioStore store, CancellationToken ct)
    {
        // Deterministic IDs make explicit reruns safe and never overwrite owner edits.
        var names = new[] { "i21Connect — Risk Management Module", "PruOne", "PruLive Dashboard", "eQuotes", "IoT Plastic Bottle Wi-Fi Vendo", "Barangay Flood Monitoring System" };
        var summaries = new[] { "An illustrative case study in enterprise risk workflows and maintainable application design.", "An illustrative enterprise application focused on clear workflows and reliable service boundaries.", "A dashboard concept for turning operational information into a clear, usable interface.", "A quotation workflow concept that brings structured inputs and clear outputs together.", "An academic IoT concept connecting bottle collection with a Wi-Fi access workflow.", "An academic monitoring concept for presenting local water-level observations." };
        for (var i = 0; i < names.Length; i++)
        {
            var id = "sample-project-" + i;
            if (await store.Project(id, ct) != null) continue;
            await store.SaveProject(new Project { Id = id, Title = names[i], Slug = PortfolioService.Slug(names[i]), Summary = summaries[i], Description = "Illustrative sample content. Replace with your approved case study before publishing this portfolio. No employer-confidential implementation, screenshot, claim or business metric is represented here.", ProblemStatement = "Describe the approved business or community problem here.", Solution = "Describe your contribution using information you are permitted to share.", Role = "Full Stack Software Engineer (sample — verify)", Architecture = "Replace with an approved high-level architecture overview.", Technologies = i < 4 ? ["C#", "ASP.NET Core", "SQL Server", "React"] : ["IoT", "JavaScript"], Visibility = Visibility.Published, ProjectStatus = ProjectStatus.Completed, IsFeatured = i < 3, SortOrder = i }, 0, ct);
        }
        var items = new List<ContentItem>
        {
            new() { Id = "settings", Kind = "settings", Title = "Jay Carlo C. Ribaya", Subtitle = "Full Stack Software Engineer", Content = "I build thoughtful interfaces and dependable systems. From enterprise applications to connected devices, I turn complex problems into software that feels simple." },
            new() { Id = "section-hero", Kind = "sections", Key = "hero", Title = "Thoughtful software.\nBuilt for the real world.", Subtitle = "FULL STACK SOFTWARE ENGINEER", Content = "Hi, I’m Jay Carlo. I connect thoughtful interfaces with robust backend systems to create software that works beautifully.", SortOrder = 0 },
            new() { Id = "section-about", Kind = "sections", Key = "about", Title = "An engineer with an eye for the whole picture.", Content = "I’m Jay Carlo C. Ribaya, a Full Stack Software Engineer working across C#, ASP.NET Core, React, JavaScript and TypeScript. My interests span enterprise applications, cloud services, and the details that make software useful.", SortOrder = 1 },
            new() { Id = "section-projects", Kind = "sections", Key = "projects", Title = "Selected work", Content = "A closer look at the systems, interfaces, and ideas I’ve worked on. The case studies below are editable illustrative samples.", SortOrder = 2 },
            new() { Id = "section-skills", Kind = "sections", Key = "skills", Title = "The right tools.\nA strong foundation.", Content = "Across the stack, from the first interaction to the last database query.", SortOrder = 3 },
            new() { Id = "section-experience", Kind = "sections", Key = "experience", Title = "Experience & growth", Content = "A place for the work, lessons, and milestones along the way.", SortOrder = 4 },
            new() { Id = "section-contact", Kind = "sections", Key = "contact", Title = "Let’s build something meaningful.", Content = "Have a project in mind or a problem worth solving? I’d love to hear about it.", Configuration = new() { ButtonLabel = "Start a conversation", ButtonUrl = "/contact" }, SortOrder = 5 },
            new() { Id = "section-social", Kind = "sections", Key = "social", Title = "Elsewhere", Content = "Add your verified professional links in Site settings.", SortOrder = 6 }
        };
        var skills = new[] { ("C# / ASP.NET Core", "Backend engineering"), ("React / TypeScript", "Frontend development"), ("SQL Server / MongoDB", "Data & persistence"), ("Azure / Docker", "Cloud & delivery") };
        items.AddRange(skills.Select((s,i) => new ContentItem { Id = "skill-" + i, Kind = "skills", Title = s.Item1, Subtitle = s.Item2, Content = "Editable skill profile — add your level and practical experience.", SortOrder = i }));
        items.Add(new() { Id = "experience-sample", Kind = "experience", Title = "Full Stack Software Engineer", Subtitle = "Professional experience — replace with verified details", Content = "Add your employer, dates, responsibilities, and approved contributions here. This entry is a placeholder, not a claim about employment." });
        items.Add(new() { Id = "education-sample", Kind = "education", Title = "Education", Content = "Add your verified degree, institution, and graduation date." });
        items.Add(new() { Id = "category-enterprise", Kind = "categories", Title = "Enterprise" });
        items.Add(new() { Id = "category-iot", Kind = "categories", Title = "IoT & community" });
        foreach (var item in items) if (await store.ContentItem(item.Id, ct) == null) await store.SaveContent(item, 0, ct);
    }
}
