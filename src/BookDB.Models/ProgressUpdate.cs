using System;

namespace BookDB.Models;

/// <summary>
/// A step-based progress update for a long-running operation (backup, CSV export). <typeparamref name="TStep"/>
/// is the operation's step enum; <see cref="Current"/> / <see cref="Total"/> carry a count for the steps that
/// report one (e.g. cover images exported) and are 0 otherwise. The presentation layer maps the step to a
/// localized string, so the emitting Logic/Data layer stays free of localization.
/// </summary>
public readonly record struct ProgressUpdate<TStep>(TStep Step, int Current = 0, int Total = 0)
    where TStep : struct, Enum;
