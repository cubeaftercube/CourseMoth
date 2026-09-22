// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// A course topic. Flat, with no hierarchy — nested categories are a temptation
/// that adds nothing in the MVP and complicates sync. See DomainMap §4.1.
/// </summary>
public class Category
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public int Order { get; set; }
}
