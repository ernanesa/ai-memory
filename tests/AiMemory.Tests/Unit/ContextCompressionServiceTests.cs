using AiMemory.Services;
using FluentAssertions;
using Xunit;

namespace AiMemory.Tests.Unit;

public class ContextCompressionServiceTests
{
    [Fact]
    public void CompressCode_WithLargeUsingsBlock_CollapsesToOmittedMarker()
    {
        var content = string.Join("\n",
        [
            "using System;",
            "using System.IO;",
            "using System.Linq;",
            "using System.Text;",
            "",
            "public class Foo { }"
        ]);

        var result = ContextCompressionService.CompressCode(content, "csharp", null);

        result.Should().Contain("[usings omitted]");
        result.Should().NotContain("using System;");
        result.Should().Contain("public class Foo { }");
    }

    [Fact]
    public void CompressCode_WithSmallUsingsBlock_PreservesUsings()
    {
        var content = string.Join("\n",
        [
            "using System;",
            "using System.IO;",
            "",
            "public class Foo { }"
        ]);

        var result = ContextCompressionService.CompressCode(content, "csharp", null);

        result.Should().Contain("using System;");
        result.Should().NotContain("[usings omitted]");
    }

    [Fact]
    public void CompressCode_WithCopyrightHeader_ReplacesWithOmittedMarker()
    {
        var content = string.Join("\n",
        [
            "// Copyright (c) 2024 Acme Corp",
            "// All rights reserved.",
            "",
            "public class Foo { }"
        ]);

        var result = ContextCompressionService.CompressCode(content, "csharp", null);

        result.Should().Contain("[license header omitted]");
        result.Should().NotContain("Copyright (c) 2024 Acme Corp");
        result.Should().Contain("public class Foo { }");
    }

    [Fact]
    public void CompressCode_WithEmptyString_ReturnsEmpty()
    {
        var result = ContextCompressionService.CompressCode("", "csharp", null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void CompressCode_WithWhitespaceOnly_ReturnsInputUnchanged()
    {
        var input = "   \n\t ";
        var result = ContextCompressionService.CompressCode(input, "csharp", null);
        result.Should().Be(input);
    }

    [Fact]
    public void CompressCode_WithSimpleCodeWithoutUsingsOrHeaders_IsPreserved()
    {
        var content = "public class Calculator\n{\n    public int Add(int a, int b) => a + b;\n}";

        var result = ContextCompressionService.CompressCode(content, "csharp", null);

        result.Should().Contain("public class Calculator");
        result.Should().Contain("public int Add(int a, int b) => a + b;");
        result.Should().NotContain("omitted");
    }
}
