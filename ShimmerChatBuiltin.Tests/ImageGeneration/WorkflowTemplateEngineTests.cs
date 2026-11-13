using Newtonsoft.Json.Linq;
using ShimmerChatBuiltin.ImageGeneration;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class WorkflowTemplateEngineTests
{
    [Fact]
    public void FindPlaceholders_ReturnsNamesInOrder()
    {
        var (names, error) = WorkflowTemplateEngine.FindPlaceholders(
            """{"a":"{{prompt}}","b":{{steps}}}""");

        error.Should().BeNull();
        names.Should().Equal("prompt", "steps");
    }

    [Fact]
    public void FindPlaceholders_Unterminated_ReturnsError()
    {
        var (names, error) = WorkflowTemplateEngine.FindPlaceholders("""{"a":"{{prompt}""");

        names.Should().BeNull();
        error.Should().Contain("Unterminated");
    }

    [Fact]
    public void FindPlaceholders_InvalidName_ReturnsError()
    {
        var (_, error) = WorkflowTemplateEngine.FindPlaceholders("""{"a":"{{not valid}}"}""");

        error.Should().Contain("Invalid placeholder");
    }

    [Fact]
    public void Render_EscapesInsideStringsAndEmitsLiteralsOutside()
    {
        var template = """
            {
              "text": "{{text}}",
              "count": {{count}},
              "flag": {{flag}},
              "nested": "{{count}}",
              "quoted": "{{quote}}"
            }
            """;

        var values = new Dictionary<string, object>
        {
            ["text"] = "he said \"hi\"\nline 2",
            ["count"] = 5L,
            ["flag"] = true,
            ["quote"] = "a\\b"
        };

        var (workflow, error) = WorkflowTemplateEngine.Render(template, values);

        error.Should().BeNull();
        workflow!["text"]!.Value<string>().Should().Be("he said \"hi\"\nline 2");
        workflow["count"]!.Value<long>().Should().Be(5);
        workflow["flag"]!.Value<bool>().Should().BeTrue();
        workflow["nested"]!.Value<string>().Should().Be("5");
        workflow["quoted"]!.Value<string>().Should().Be("a\\b");
    }

    [Fact]
    public void Render_UndefinedPlaceholder_ReturnsError()
    {
        var (workflow, error) = WorkflowTemplateEngine.Render(
            """{"a":"{{missing}}"}""", new Dictionary<string, object>());

        workflow.Should().BeNull();
        error.Should().Contain("undeclared placeholder 'missing'");
    }

    [Fact]
    public void Bind_UsesDeclaredDefaultsForOptionalParameters()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":7}"""));

        error.Should().BeNull();
        values!["steps"].Should().Be(24L);
        values["style"].Should().Be("anime");
    }

    [Fact]
    public void Bind_MissingRequiredParameter_Fails()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"model":"sdxl","seed":7}"""));

        values.Should().BeNull();
        error.Should().Contain("Missing required parameter 'prompt'");
    }

    [Fact]
    public void Bind_UnknownParameter_Fails()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":7,"surprise":1}"""));

        values.Should().BeNull();
        error.Should().Contain("Unknown parameter(s): surprise");
    }

    [Fact]
    public void Bind_NumericString_IsAccepted()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":"7","steps":"30"}"""));

        error.Should().BeNull();
        values!["seed"].Should().Be(7L);
        values["steps"].Should().Be(30L);
    }

    [Fact]
    public void Bind_OutOfRangeInteger_Fails()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":7,"steps":999}"""));

        values.Should().BeNull();
        error.Should().Contain("must be <= 150");
    }

    [Fact]
    public void Bind_EnumIsCaseInsensitiveAndNormalized()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":7,"style":"PHOTO"}"""));

        error.Should().BeNull();
        values!["style"].Should().Be("photo");
    }

    [Fact]
    public void Bind_InvalidEnum_Fails()
    {
        var parameters = ImageGenerationTestData.CreateCustomWorkflow().Parameters;

        var (values, error) = WorkflowTemplateEngine.Bind(
            parameters,
            JObject.Parse("""{"prompt":"a cat","model":"sdxl","seed":7,"style":"sketch"}"""));

        values.Should().BeNull();
        error.Should().Contain("must be one of");
    }
}
