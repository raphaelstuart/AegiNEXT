using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SystemFontCatalogTests
{
    [Fact]
    public void MissingFontTablesAndStyleNameProduceASaveableExplicitVariant()
    {
        var source = new SystemFontStyleSource { SourceFamilyName = "Example", FamilyName = "Example", PostScriptName = string.Empty };
        var face = Assert.Single(SystemFontCatalog.CreateFaces(new(), source));
        Assert.Equal("Regular", face.Variant.Name);
        Assert.Null(face.Variant.PostScriptName);
        ProjectValidator.ValidateSubtitleStyle(new() { FontFamily = face.FamilyName, FontVariant = face.Variant });
        face = Assert.Single(SystemFontCatalog.CreateFaces(new() { Weight = 437 }, source with { Weight = 947, Italic = true }));
        Assert.Equal("Weight 437 Italic", face.Variant.Name);
        Assert.Equal(437, face.Variant.Weight);
        ProjectValidator.ValidateSubtitleStyle(new() { FontFamily = face.FamilyName, FontVariant = face.Variant });
    }

    [Fact]
    public void DefaultVariableInstanceIsIncludedEvenWithoutNamedRecords()
    {
        var metadata = new OpenTypeFontMetadata
        {
            FamilyName = "Example", Axes = [new(OpenTypeFontMetadataReader.WEIGHT_TAG, 100, 400, 900)]
        };
        var face = Assert.Single(SystemFontCatalog.CreateFaces(metadata, new() { SourceFamilyName = "Example", Weight = 947 }));
        Assert.Equal("Regular", face.Variant.Name);
        Assert.Equal(400, face.Variant.Weight);
        Assert.Equal(0, face.NamedInstanceIndex);
        Assert.True(face.IsVariable);
    }

    [Fact]
    public void InvalidNativeIdentifiersCannotEnterTheSaveableCatalog()
    {
        var source = new SystemFontStyleSource { FamilyName = "Example", SourceFamilyName = "Example" };
        Assert.Throws<InvalidDataException>(() => SystemFontCatalog.CreateFaces(new(), source with { FamilyName = "Invalid\nFamily" }));
        Assert.Throws<InvalidDataException>(() => SystemFontCatalog.CreateFaces(new(), source with { StyleName = "\n" }));
        Assert.Throws<InvalidDataException>(() => SystemFontCatalog.CreateFaces(new(), source with { PostScriptName = new string('A', 513) }));
    }

    [Fact]
    public void VariableFixtureEnumeratesNamedInstancesWithLogicalWeights()
    {
        using var typeface = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var faces = SystemFontCatalog.ReadTypeface(typeface, "Noto Sans", 0, "Regular");
        Assert.Equal(9, faces.Length);
        var black = Assert.Single(faces, face => face.Variant.Name == "Black");
        Assert.Equal("Noto Sans Black", black.DisplayName);
        Assert.Equal("NotoSans-Black", black.Variant.PostScriptName);
        Assert.Equal(900, black.Variant.Weight);
        Assert.Equal(5, black.Variant.Width);
        Assert.Equal(9, black.NamedInstanceIndex);
        Assert.Equal(0, black.CollectionIndex);
        Assert.True(black.IsVariable);
        Assert.False(black.IsStyleEnumerated);
        Assert.True(Assert.Single(faces, face => face.Variant.Name == "Regular").IsStyleEnumerated);
    }

    [Fact]
    public void StaticDuplicateWinsWithoutLosingFamilyAliases()
    {
        var variant = new SubtitleFontVariant { Name = "Black", PostScriptName = "Example-Black", Weight = 900 };
        var variable = new SystemFontFace
        {
            FamilyName = "Example", Aliases = ["Example", "Example VF"], Variant = variant,
            IsVariable = true, SourceFamilyName = "Example VF", StyleIndex = 8, NamedInstanceIndex = 9
        };
        var independent = variable with
        {
            Aliases = ["Example", "Example Black"], IsVariable = false, SourceFamilyName = "Example Black",
            StyleIndex = 0, NamedInstanceIndex = 0, IsStyleEnumerated = true
        };
        var catalog = new SystemFontCatalog([variable, independent]);
        var face = Assert.Single(catalog.Faces);
        Assert.False(face.IsVariable);
        Assert.Equal("Example Black", face.SourceFamilyName);
        Assert.Contains("Example VF", face.Aliases);
        Assert.Contains("Example Black", face.Aliases);
    }

    [Fact]
    public void EnumeratedStyleWinsOverSyntheticCandidateFromSameVariableFont()
    {
        var candidate = new SystemFontFace
        {
            FamilyName = "Example", Aliases = ["Example"], Variant = new() { Name = "Text", Weight = 437 },
            IsVariable = true, SourceFamilyName = "Example", NamedInstanceIndex = 1
        };
        var catalog = new SystemFontCatalog([candidate, candidate with { StyleIndex = 4, IsStyleEnumerated = true }]);
        Assert.Equal(4, Assert.Single(catalog.Faces).StyleIndex);
    }

    [Fact]
    public void SameNumericWeightDoesNotMergeDifferentDesignerVariants()
    {
        var first = new SystemFontFace
        {
            FamilyName = "Example", Aliases = ["Example"], SourceFamilyName = "Example", Variant = new() { Name = "Text", Weight = 437 }
        };
        var catalog = new SystemFontCatalog([first, first with { Variant = first.Variant with { Name = "Display" } }]);
        Assert.Equal(2, catalog.Faces.Length);
    }

    [Fact]
    public void SystemEnumerationReturnsManagedSnapshotWithActualStyleLocators()
    {
        var manager = SKFontManager.Default;
        var families = manager.GetFontFamilies().Where(family => !string.IsNullOrWhiteSpace(family)).Take(5).ToArray();
        Assert.NotEmpty(families);
        var catalog = new SystemFontCatalog(manager, families);
        Assert.NotEmpty(catalog.Faces);
        Assert.All(catalog.Faces, face =>
        {
            Assert.False(string.IsNullOrWhiteSpace(face.FamilyName));
            Assert.Contains(face.FamilyName, face.Aliases);
            Assert.InRange(face.Variant.Weight, 1, 1000);
            Assert.InRange(face.Variant.Width, 1, 9);
            using var styles = manager.GetFontStyles(face.SourceFamilyName);
            Assert.InRange(face.StyleIndex, 0, styles.Count - 1);
        });
    }

    [MacInstalledNotoFontFact]
    public void InstalledNotoFamiliesPreferStaticDuplicatesAndKeepVariableInstanceLocators()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Sans SC", "Noto Sans", "Noto Serif SC"]);
        Assert.Empty(catalog.Issues);
        var scSemiBold = Assert.Single(catalog.Faces, face => face.FamilyName == "Noto Sans SC" && face.Variant.Name == "SemiBold");
        var scBlack = Assert.Single(catalog.Faces, face => face.FamilyName == "Noto Sans SC" && face.Variant.Name == "Black");
        Assert.False(scSemiBold.IsVariable);
        Assert.False(scBlack.IsVariable);
        Assert.Equal(600, scSemiBold.Variant.Weight);
        Assert.Equal(900, scBlack.Variant.Weight);
        foreach (var family in new[] { "Noto Sans", "Noto Serif SC" })
        {
            var semiBold = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == "SemiBold");
            var black = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == "Black");
            Assert.True(semiBold.IsVariable);
            Assert.True(black.IsVariable);
            Assert.Equal(600, semiBold.Variant.Weight);
            Assert.Equal(900, black.Variant.Weight);
            Assert.True(semiBold.IsStyleEnumerated);
            Assert.True(black.IsStyleEnumerated);
        }
        Assert.All(catalog.Faces.Where(face => face.IsVariable && face.IsStyleEnumerated), AssertVariableLocator);
    }

    private static void AssertVariableLocator(SystemFontFace face)
    {
        using var styles = SKFontManager.Default.GetFontStyles(face.SourceFamilyName);
        using var typeface = styles.CreateTypeface(face.StyleIndex);
        var metadata = OpenTypeFontMetadataReader.Read(typeface);
        var instance = Assert.Single(metadata.NamedInstances, instance => instance.Index == face.NamedInstanceIndex);
        Assert.Equal(instance.Name, face.Variant.Name);
        Assert.Equal((int)instance.Coordinates[OpenTypeFontMetadataReader.WEIGHT_TAG], face.Variant.Weight);
        var styleName = string.Concat(styles.GetStyleName(face.StyleIndex).Where(char.IsLetterOrDigit));
        var variantName = string.Concat(face.Variant.Name.Where(char.IsLetterOrDigit));
        using var stream = typeface.OpenStream(out var fontIndex);
        Assert.True(string.Equals(styleName, variantName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeface.PostScriptName, face.Variant.PostScriptName, StringComparison.OrdinalIgnoreCase)
            || fontIndex >>> 16 == face.NamedInstanceIndex,
            $"{face.DisplayName} does not match its native style locator {face.SourceFamilyName}:{face.StyleIndex} ({styleName}).");
    }
}
