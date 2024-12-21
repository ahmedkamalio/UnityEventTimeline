# Contributing to UnityEventTimeline

Thank you for your interest in contributing to UnityEventTimeline! This document provides guidelines and instructions
for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Making Contributions](#making-contributions)
- [Coding Standards](#coding-standards)
- [Testing Guidelines](#testing-guidelines)
- [Documentation](#documentation)
- [Pull Request Process](#pull-request-process)
- [Release Process](#release-process)

## Code of Conduct

This project and everyone participating in it are governed by our Code of Conduct. By participating, you are expected to
uphold this code. Please report unacceptable behavior to [@ahmedkamalio](https://github.com/ahmedkamalio).

## Getting Started

1. Fork the repository
2. Clone your fork locally
3. Create a new branch for your changes
4. Make your changes following our guidelines
5. Submit a pull request

## Development Setup

### Prerequisites

- Unity 2021.3 or higher
- Visual Studio or JetBrains Rider
- Git

### Setting Up the Development Environment

1. Clone the repository:

   ```bash
   git clone https://github.com/ahmedkamalio/UnityEventTimeline.git
   ```

2. Open the project in Unity:
    - Use Unity Hub to add the project
    - Select the correct Unity version
    - Open the project

3. Install development dependencies:
    - Ensure NUnit is properly set up for testing
    - Install the recommended Unity packages for development

## Making Contributions

### Types of Contributions

We welcome the following types of contributions:

- Bug fixes
- Performance improvements
- Feature enhancements
- Documentation improvements
- Test coverage improvements

### Branch Naming Convention

- Feature branches: `feature/description`
- Bug fix branches: `fix/description`
- Documentation branches: `docs/description`
- Performance improvement branches: `perf/description`

## Coding Standards

### General Guidelines

- Follow C# coding conventions
- Use nullable reference types (`#nullable enable`)
- Write clear, self-documenting code
- Keep methods focused and concise
- Use meaningful variable and method names

### Code Style

```csharp
#nullable enable

namespace UnityEventTimeline
{
    public class ExampleClass
    {
        private readonly int _privateField;
        public int PublicProperty { get; set; }

        public void ExampleMethod(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
            {
                throw new ArgumentException("Parameter cannot be null or empty", nameof(parameter));
            }

            // Implementation
        }
    }
}
```

### Documentation Style

- Use XML documentation for all public APIs
- Include example usage where appropriate
- Document thread safety considerations
- Keep documentation up to date with changes

```csharp
/// <summary>
/// Represents an example event in the timeline.
/// </summary>
/// <remarks>
/// This event demonstrates proper documentation style.
/// </remarks>
public class ExampleEvent : TimelineEvent<ExampleEvent>
{
    /// <summary>
    /// Gets or sets the data associated with this event.
    /// </summary>
    public string? Data { get; set; }

    protected override void Execute()
    {
        // Implementation
    }
}
```

## Testing Guidelines

### Required Tests

All new code should include:

1. Unit tests for new functionality
2. Integration tests for system interactions
3. Performance tests for critical paths

### Test Structure

```csharp
[Test]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    var sut = new SystemUnderTest();

    // Act
    var result = sut.MethodToTest();

    // Assert
    Assert.That(result, Is.EqualTo(expectedValue));
}
```

### Performance Testing

- Include performance tests for critical operations
- Document performance expectations
- Test with various data sizes and scenarios

## Documentation

### Required Documentation

1. XML documentation for all public APIs
2. README updates for new features
3. Example usage in documentation
4. Update changelog

### Documentation Style Guide

- Use clear, concise language
- Include code examples
- Document thread safety considerations
- Explain complex concepts gradually

## Pull Request Process

1. Create a descriptive pull request title
2. Fill out the pull request template completely
3. Ensure all tests pass
4. Update documentation
5. Request review from maintainers

### Pull Request Template

```markdown
## Description

[Describe your changes here]

## Type of Change

- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing

- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing performed

## Documentation

- [ ] XML documentation updated
- [ ] README updated
- [ ] Documentation files updated
- [ ] Examples updated

## Related Issues

[Link to related issues here]
```

## Release Process

1. Version Bump
    - Update version numbers in code
    - Update package.json
    - Update documentation version references

2. Changelog Update
    - Add new version section
    - Document all changes
    - Categorize changes (Added, Changed, Deprecated, Removed, Fixed)

3. Release Checklist
    - [ ] All tests passing
    - [ ] Documentation updated
    - [ ] Version numbers updated
    - [ ] Changelog updated
    - [ ] Release notes prepared

### Version Numbering

We follow semantic versioning (MAJOR.MINOR.PATCH):

- MAJOR: Breaking changes
- MINOR: New features, no breaking changes
- PATCH: Bug fixes, no breaking changes

## Questions?

If you have questions about contributing, feel free to:

1. Open an issue with your question
2. Contact the maintainers
3. Ask in the community forums

Thank you for contributing to UnityEventTimeline!
