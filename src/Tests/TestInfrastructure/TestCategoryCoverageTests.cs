using System.Reflection;

namespace CodexCliPlus.Tests.TestInfrastructure;

[Trait("Category", "Fast")]
public sealed class TestCategoryCoverageTests
{
    private static readonly string[] AllowedCategories =
    [
        "Fast",
        "LocalIntegration",
        "Packaging",
        "Smoke",
        "LiveBackend",
    ];

    [Fact]
    public void TestClassesDeclareExactlyOneKnownCategory()
    {
        var testClasses = typeof(TestCategoryCoverageTests)
            .Assembly.GetTypes()
            .Where(type => type.IsClass && HasTestMethods(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal);

        var failures = new List<string>();
        foreach (var testClass in testClasses)
        {
            var classCategories = ReadCategoryTraits(testClass.GetCustomAttributes(inherit: false))
                .ToArray();

            if (classCategories.Length != 1)
            {
                failures.Add(
                    $"{testClass.FullName} declares {classCategories.Length} class-level Category traits."
                );
                continue;
            }

            if (!AllowedCategories.Contains(classCategories[0], StringComparer.Ordinal))
            {
                failures.Add(
                    $"{testClass.FullName} uses unknown test Category '{classCategories[0]}'."
                );
            }

            foreach (var method in GetTestMethods(testClass))
            {
                var methodCategories = ReadCategoryTraits(
                        method.GetCustomAttributes(inherit: false)
                    )
                    .ToArray();
                if (methodCategories.Length > 0)
                {
                    failures.Add(
                        $"{testClass.FullName}.{method.Name} declares method-level Category traits."
                    );
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool HasTestMethods(Type type)
    {
        return GetTestMethods(type).Any();
    }

    private static IEnumerable<MethodInfo> GetTestMethods(Type type)
    {
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttributes(inherit: false).Any(IsXunitTestAttribute));
    }

    private static bool IsXunitTestAttribute(object attribute)
    {
        return attribute.GetType().Name is "FactAttribute" or "TheoryAttribute";
    }

    private static IEnumerable<string> ReadCategoryTraits(IEnumerable<object> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (
                !string.Equals(attribute.GetType().Name, "TraitAttribute", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var name = ReadStringProperty(attribute, "Name");
            if (!string.Equals(name, "Category", StringComparison.Ordinal))
            {
                continue;
            }

            var value = ReadStringProperty(attribute, "Value");
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string? ReadStringProperty(object attribute, string propertyName)
    {
        return attribute.GetType().GetProperty(propertyName)?.GetValue(attribute) as string;
    }
}
