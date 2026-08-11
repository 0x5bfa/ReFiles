# Change notifications

Change sources keep an active browse state synchronized with provider/filesystem mutations after initial enumeration.

## Goals

- reconcile changes without unconditional full reloads;
- preserve stable identity/selection where possible;
- tolerate bursts and races;
- reject notifications belonging to stale locations/generations;
- keep provider-specific notification mechanisms below generic browsing.

## Change model

A provider change source should translate backend events into semantic mutations such as add, remove, rename/move, or refresh-required when a precise diff cannot be produced.

## Races

Notifications can arrive while enumeration, property retrieval, thumbnails, or navigation replacement are active. Every application of a change must verify that the target browse state is still current.

## Bursts

Filesystems and operations can produce many related events. Coalesce/debounce where semantics allow instead of forwarding every low-level notification directly to the UI dispatcher.

## Identity

Use stable identity to recognize rename/move and preserve selection/ViewModel state. Case-only rename and provider-specific case rules deserve explicit tests.

## Fallback

A full refresh is a valid correctness fallback when a bounded reliable diff cannot be constructed, but it should not be the default response to every low-level event.

## Tests

Cover create/delete/rename/case-only rename/move bursts, changes during enumeration, stale-generation events, selected-item changes, provider watcher disposal, and fallback refresh behavior.
