// Copyright (c) Microsoft. All rights reserved.

namespace SharedWebComponents.Models;
public record class ProblemDetails
{
    public string? Type { get; set; }

    public string? Title { get; set; }

    public int? Status { get; set; }

    public string? Detail { get; set; }
    public string? Instance { get; set; }
}
