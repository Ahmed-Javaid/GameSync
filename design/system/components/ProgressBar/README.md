Thin `primary` bar on a `bg-400` track, with an optional mono label and value.

Provide `value` (0 to 100), `label` and `right` (defaults to the percentage). Used for zip packing, moving the backup folder, sync progress and storage use.

- No `value`: how much there is isn't known yet (a scan starting, a folder being listed), so a short stripe slides along the track until it is (KAN-80). With reduced motion the track fills faintly and stays still; the words beside it keep changing.
- `tone` (`ok`, `warn`, `neutral`) colours the fill for a job that's done, failed or paused. For a job under way with a title, speed and time left, use `JobProgress`.
