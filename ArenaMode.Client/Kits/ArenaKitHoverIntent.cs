using System;

namespace ArenaMode.Kits;

/// <summary>
/// Which card the kit browser's detail pane shows while the pointer moves over the cards
/// (docs/reviews/KIT-PICKER-20261110.md, "Hover"). The pane sits to the right of every card,
/// so the way from a card to its COPY TO MY KITS, EDIT or DELETE buttons crosses other
/// cards. Showing every card the pointer touched swapped the pane to one of those on the
/// way (2026-11-10, the owner's report). A card takes the pane only when the pointer comes
/// to rest on it, or stays on it for a while without heading right towards the pane; a
/// click shows its card at once. Unity-free, so tools/KitPickerChecks runs it offline: the
/// view feeds it the unscaled time and the pointer's screen position every frame.
/// </summary>
internal sealed class ArenaKitHoverIntent<T> where T : class
{
    /// <summary>The pointer counts as resting once it has stayed this long within <see cref="RestDistance"/>.</summary>
    internal const float RestSeconds = .10f;

    /// <summary>Screen pixels the pointer may drift and still rest; a hand on the mouse is never perfectly still.</summary>
    internal const float RestDistance = 6f;

    /// <summary>On a card this long without heading right, it is shown even while the pointer keeps creeping.</summary>
    internal const float DwellSeconds = .45f;

    /// <summary>The window over which the pointer's direction is measured.</summary>
    internal const float HeadingSeconds = .08f;

    /// <summary>
    /// Heading right means moving right at least this many pixels per window and no more
    /// than <see cref="HeadingSlope"/> times as far up or down: the pane spans the browser's
    /// full height, and its buttons sit at its bottom, below most cards.
    /// </summary>
    internal const float HeadingMinimum = 2f;
    internal const float HeadingSlope = 4f;

    private T _over;
    private float _enteredAt;
    private float _stillX, _stillY, _stillSince;
    private float _sampleX, _sampleY, _sampleAt;
    private bool _headingRight;
    private bool _tracking;

    /// <summary>The card the pane shows; null until the pointer rests on a card or a card is clicked.</summary>
    internal T Shown { get; private set; }

    /// <summary>The pointer entered <paramref name="card"/>.</summary>
    internal void Enter(T card, float now)
    {
        _over = card;
        _enteredAt = now;
    }

    /// <summary>
    /// The pointer left <paramref name="card"/>. Leaving to the pane must not hand it the last
    /// card crossed; an exit that arrives after the next card's enter does not clear that one.
    /// </summary>
    internal void Exit(T card)
    {
        if (card != null && ReferenceEquals(_over, card)) _over = null;
    }

    /// <summary>A click: the card is shown at once.</summary>
    internal void Show(T card)
    {
        Shown = card;
    }

    /// <summary>
    /// A rebuilt or closed browser: its cards are gone, and a destroyed card sends no exit. The pointer's
    /// motion is kept; it did not change with the cards.
    /// </summary>
    internal void Reset()
    {
        _over = null;
        Shown = null;
    }

    /// <summary>One frame. True when <see cref="Shown"/> changed and the pane is to be redrawn.</summary>
    internal bool Update(float now, float x, float y)
    {
        if (!_tracking)
        {
            _tracking = true;
            _stillX = _sampleX = x;
            _stillY = _sampleY = y;
            _stillSince = _sampleAt = now;
            _headingRight = false;
        }

        float driftX = x - _stillX, driftY = y - _stillY;
        if (driftX * driftX + driftY * driftY > RestDistance * RestDistance)
        {
            _stillX = x;
            _stillY = y;
            _stillSince = now;
        }

        if (now - _sampleAt >= HeadingSeconds)
        {
            float dx = x - _sampleX, dy = y - _sampleY;
            _headingRight = dx >= HeadingMinimum && Math.Abs(dy) <= dx * HeadingSlope;
            _sampleX = x;
            _sampleY = y;
            _sampleAt = now;
        }

        if (_over == null || ReferenceEquals(_over, Shown)) return false;
        bool resting = now - _stillSince >= RestSeconds;
        bool dwelt = now - _enteredAt >= DwellSeconds && !_headingRight;
        if (!resting && !dwelt) return false;
        Shown = _over;
        return true;
    }
}
