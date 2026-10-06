using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace ArchitectureTests;

public sealed class MigrationSafetyTests
{
	[Test]
	public void NarrowingMigrations_ShouldPrecedeAlterColumnWithATruncationPreCheck()
	{
		var migrationsDir = FindMigrationsDirectory();

		var violations = Directory.GetFiles(migrationsDir, "*.cs")
			.Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)
				&& !Path.GetFileName(f).Equals("ApplicationDbContextModelSnapshot.cs", StringComparison.Ordinal))
			.SelectMany(file => FindViolationsInFile(file))
			.ToList();

		violations.Should().BeEmpty(
			"a narrowing AlterColumn<string> (unbounded -> maxLength) must be preceded in the same " +
			"migration's Up() by a migrationBuilder.Sql(...) pre-check that truncates/validates existing " +
			"data - see AddVolunteerOpportunityTitleDescriptionMaxLength for the pattern. Violations:\n" +
			string.Join("\n", violations));
	}

	private static IEnumerable<string> FindViolationsInFile(string file)
	{
		var upMethod = ExtractUpMethod(File.ReadAllText(file));
		if (upMethod is null)
			yield break;

		foreach (Match call in Regex.Matches(upMethod, @"migrationBuilder\.AlterColumn<string>\s*\((?<args>[^;]*?)\);", RegexOptions.Singleline))
		{
			var args = call.Groups["args"].Value;
			var hasMaxLength = Regex.IsMatch(args, @"maxLength:\s*\d+");
			var hasOldMaxLength = Regex.IsMatch(args, @"oldMaxLength:\s*\d+");

			if (!hasMaxLength || hasOldMaxLength)
				continue;

			var precedingUpText = upMethod[..call.Index];
			if (!precedingUpText.Contains("migrationBuilder.Sql(", StringComparison.Ordinal))
				yield return $"{Path.GetFileName(file)}: narrows a column with no preceding data pre-check.";
		}
	}

	private static string? ExtractUpMethod(string content)
	{
		var upStart = content.IndexOf("protected override void Up(", StringComparison.Ordinal);
		if (upStart < 0)
			return null;

		var downStart = content.IndexOf("protected override void Down(", StringComparison.Ordinal);
		var upEnd = downStart > upStart ? downStart : content.Length;

		return content[upStart..upEnd];
	}

	private static string FindMigrationsDirectory()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Afunto.slnx")))
			dir = dir.Parent;

		if (dir is null)
			throw new InvalidOperationException($"Could not locate Afunto.slnx above {AppContext.BaseDirectory}.");

		var migrationsDir = Path.Combine(dir.FullName, "src", "Infrastructure", "Persistence", "Migrations");
		if (!Directory.Exists(migrationsDir))
			throw new InvalidOperationException($"Migrations directory not found at {migrationsDir}.");

		return migrationsDir;
	}
}
