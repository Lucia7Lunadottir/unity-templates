# Day and Night (1.0.0)

- `DayNightTime`: the cycle. Day, night and transition lengths in minutes, `nightLerp` (0 day, 1 night), `currentHour`, `currentDay`, events `OnNightStarted`, `OnNightEnded`, `dayChanged`, config modes (`Config`) and saving to `DayNight.json` in the current save folder. Time runs on `GameTime`, so sleeping can speed it up.
- `DayNightSystem`: `DayNightTime` plus the look: sun intensity, fog colour, skybox exposure and ambient light for day and night.
- `DayNightTimeChanger`: a dropdown that switches the config.

Put `DayNightSystem` on one object in the scene, assign the sun light.

## Bridge
Cold resistance by time of day: put components that implement `IResilienceTarget` (one property `resilienceTime`) into `Temperature Systems`. Empty means temperature is not touched.

Other game systems (diary, day text, quests) subscribe to `dayChanged`, `OnNightStarted`, `OnNightEnded` in your own code.
