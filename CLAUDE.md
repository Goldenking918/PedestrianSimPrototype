# CLAUDE.md — VR Digital Twin for Pedestrian Safety

## 1. Purpose of this document

This document provides the research, product, technical, and implementation context needed for Claude Code to work effectively on the Unity application for the Part 4 Software Engineering research project.

The goal is to give Claude enough context to make implementation decisions that support the research objectives, rather than treating the application as a generic VR game.

**Important source-of-truth rule:**

- The actual repository, Unity project settings, packages, scenes, prefabs, scripts, and assets are the source of truth for the **current implementation**.
- This document describes the intended research direction, architecture, requirements, constraints, and design decisions.
- Before changing existing functionality, inspect the repository and understand what is already implemented.
- Do not assume that a component described here is missing simply because it is not described in detail here.
- Do not rebuild existing functionality unnecessarily.
- If the repository conflicts with an outdated statement in this document, prefer the repository for current implementation details and flag the discrepancy where it materially affects the research design.

---

# 2. Project overview

## 2.1 Project

This is a University of Auckland Part 4 Software Engineering Research Project.

The project is being developed by a two-person team and focuses on:

> **Development and Evaluation of a VR Digital Twin for Pedestrian Safety Research**

The application is a Unity-based VR simulation of an urban pedestrian environment, centred on a real location around Symonds Street near the University of Auckland.

The system is intended to provide a controlled, repeatable environment in which pedestrian crossing behaviour can be observed and behavioural data can be collected.

The project combines:

- software engineering
- virtual reality
- digital-twin style environmental modelling
- pedestrian safety research
- controlled traffic simulation
- behavioural instrumentation
- usability evaluation

The application is a **research platform**, not a commercial game.

---

# 3. Research direction

## 3.1 Research problem

Pedestrian safety research can be difficult to conduct safely and consistently in real-world environments.

A VR environment can provide:

- controlled experimental conditions
- repeatable scenarios
- configurable traffic conditions
- safe observation of pedestrian behaviour
- detailed behavioural measurements
- a basis for future larger-scale studies

The project therefore aims to develop a realistic VR representation of an urban pedestrian environment and evaluate whether it is suitable for pedestrian safety research.

---

## 3.2 Important change in evaluation scope

The project is **not** being positioned as a large population-level behavioural study.

The practical evaluation will involve approximately **four participants**, primarily as a small formative/usability evaluation.

Therefore, the application must not be designed around claims such as:

- proving how pedestrians behave in the general population
- statistically validating a behavioural theory
- establishing population-level effects of traffic density or speed
- conclusively validating pedestrian behaviour against the real world

Instead, the evaluation should investigate whether the developed research platform is usable and technically suitable for a future larger study.

The small evaluation can provide:

- usability evidence
- feasibility evidence
- realism/presence feedback
- identification of problems with the experimental procedure
- evidence that the intended behavioural measurements can be collected
- exploratory observations of behaviour under controlled conditions
- information needed to improve the platform before a larger study

Behavioural measurements remain important even though the participant sample is small.

---

# 4. Research objectives

The project should aim to:

1. Develop a VR digital twin of a real urban pedestrian environment around Symonds Street.
2. Reproduce the visual, spatial, and traffic characteristics required for a pedestrian safety research environment.
3. Provide controlled and repeatable traffic scenarios.
4. Allow a participant to perform pedestrian crossing tasks in VR.
5. Capture behavioural measurements relevant to pedestrian crossing research.
6. Provide reliable data logging and export for later analysis.
7. Evaluate the usability and practical suitability of the platform using a small formative evaluation.
8. Identify limitations and improvements required before a larger-scale pedestrian safety study.

---

# 5. Research questions

The implementation should support the following research direction.

### RQ1

**To what extent can a VR digital twin reproduce the visual, spatial and traffic characteristics required for a realistic pedestrian safety research environment?**

This relates to:

- environmental modelling
- road geometry
- buildings and surroundings
- pedestrian infrastructure
- traffic appearance and behaviour
- scenario realism
- perceived realism/presence feedback

### RQ2

**Can participants use the VR digital twin to complete pedestrian crossing tasks without usability issues that interfere with the intended experimental procedure?**

This relates to:

- participant controls
- movement
- instructions
- task completion
- interaction
- usability
- simulator discomfort
- experimental procedure

### RQ3

**Can the VR digital twin reliably capture behavioural measures relevant to pedestrian crossing research?**

This relates directly to the application's instrumentation.

The system should be able to record appropriate measures such as:

- waiting time
- crossing duration
- accepted/rejected traffic gaps where measurable
- vehicle speed
- vehicle distance
- participant position/trajectory
- participant head orientation
- relevant head-turning behaviour where technically available
- time-to-collision or related measures where implemented
- near misses/collisions
- scenario and traffic-condition metadata
- timestamps for important events

### RQ4 — exploratory

**What preliminary behavioural differences are observable when participants experience different controlled traffic conditions?**

This is explicitly exploratory.

Do not design the system or data model around making strong causal or population-level claims from approximately four participants.

---

# 6. Research framing

The application should be described as a **VR research platform / digital twin prototype** that is evaluated through a small usability/feasibility study.

Preferred terminology:

- formative usability evaluation
- small-scale usability evaluation
- pilot usability evaluation
- research platform
- behavioural instrumentation
- exploratory behavioural observations
- controlled scenario
- preliminary findings

Avoid describing the evaluation as a large "user study" if that wording implies population-level inference.

Avoid describing the project as having conclusively "validated" real-world pedestrian behaviour.

A suitable overall project framing is:

> Develop a realistic and configurable VR digital twin of an urban pedestrian environment and evaluate its usability, realism, experimental control, and ability to collect behavioural data for future pedestrian safety research.

---

# 7. Why the software implementation matters

The software itself is a significant component of the research project.

The application should demonstrate that a VR environment can be engineered as a controlled research instrument rather than merely a visual demonstration.

Important engineering capabilities include:

- realistic environmental representation
- configurable experimental conditions
- deterministic/repeatable scenarios
- participant interaction
- traffic simulation
- event detection
- behavioural measurement
- structured data logging
- researcher configuration
- data export
- modular architecture
- maintainability
- reproducibility

Claude should therefore treat research instrumentation as a first-class requirement.

Do not optimise only for visual appearance.

---

# 8. Target environment

## 8.1 Location

The simulated environment is based around:

**Symonds Street near the University of Auckland, Auckland, New Zealand.**

The project focuses on an urban pedestrian environment containing a road and pedestrian crossing context.

The environment should preserve the important spatial relationships of the real location while remaining practical for the available VR/lab space.

## 8.2 Physical/lab constraint

The available physical lab space is approximately **12 metres**.

The intended virtual environment is approximately a **20 metre street section**, using a roughly:

- 10 metre active section
- turn-around/repositioning arrangement

The exact implementation should preserve the correct virtual scale rather than shrinking the environment to fit the physical room.

Do not assume that the participant must physically walk the entire virtual street.

---

# 9. Core application requirements

The application should provide the following major capabilities.

## 9.1 Environment

The VR scene should contain a convincing representation of the selected urban environment, including relevant:

- road
- footpaths
- crossing area
- buildings
- street furniture
- road markings
- signs
- traffic infrastructure
- environmental context

The goal is not to create an entire city.

Prioritise the area relevant to the pedestrian crossing experiment.

---

## 9.2 Participant

The participant should:

- enter the VR environment
- understand the task
- move through the pedestrian environment
- approach the crossing
- observe traffic
- decide when to cross
- complete the crossing task
- receive only the minimum necessary in-world instructions

Avoid adding unnecessary game mechanics.

---

## 9.3 Traffic

Traffic must be configurable enough to create controlled experimental conditions.

The traffic system should support relevant properties such as:

- vehicle spawning
- vehicle movement
- traffic density
- vehicle speed
- vehicle direction
- vehicle spacing
- interactions with the crossing environment
- deterministic/reproducible scenario configuration where practical

Traffic behaviour should be predictable enough for experimental use.

Avoid uncontrolled randomness where it would make scenarios difficult to reproduce.

If randomness is necessary, use configurable seeds or otherwise record the relevant scenario information.

---

# 10. Planned traffic scenarios

The project has previously considered a small set of controlled scenarios.

A useful baseline is:

| Scenario | Traffic density | Speed |
|---|---|---|
| S1 | Low | Normal |
| S2 | Medium | Normal |
| S3 | High | Normal |
| S4 | Medium | Fast |

These scenarios are intended as a manageable initial experimental set.

The architecture should make it easy to add or modify scenarios without rewriting traffic code.

Do not hard-code the experimental conditions throughout unrelated scripts.

---

# 11. Scenario configuration

Experimental conditions should be represented as data rather than being scattered through procedural code.

A scenario should be able to define relevant properties such as:

- scenario ID
- scenario name
- traffic density
- vehicle speed range
- vehicle types
- spawn configuration
- traffic direction
- duration
- environmental condition if applicable
- random seed if applicable
- other experimental parameters

A Unity `ScriptableObject`-style configuration is appropriate if it fits the existing project architecture.

The exact implementation should follow the repository's existing conventions.

---

# 12. Behavioural instrumentation

Instrumentation is one of the most important parts of the project.

The system should collect data that can later be analysed to answer the research questions.

## 12.1 Potential participant metrics

Where technically appropriate, collect:

- participant position
- participant rotation
- participant velocity
- movement trajectory
- waiting time before crossing
- crossing start time
- crossing completion time
- crossing duration
- distance travelled
- head orientation
- head turns / looking behaviour
- interaction events
- task completion

## 12.2 Traffic metrics

Where technically appropriate, collect:

- vehicle ID
- vehicle position
- vehicle velocity/speed
- vehicle direction
- spawn time
- distance from participant
- relevant closest approach
- traffic condition
- scenario ID

## 12.3 Crossing/interaction events

Important events should be explicitly logged rather than reconstructed entirely from raw position data.

Potential events include:

- scenario started
- participant ready
- participant began waiting
- participant entered crossing
- participant exited crossing
- vehicle entered relevant zone
- vehicle passed participant
- gap evaluated
- gap accepted
- gap rejected
- near miss
- collision
- scenario completed
- participant task completed

Only implement events that can be defined and detected reliably.

Do not create misleading measurements simply because a metric sounds useful.

---

# 13. Data logging

The logging system should produce structured data suitable for later analysis.

A CSV-based export is appropriate for the current project unless the existing repository uses another suitable format.

The exported data should include enough metadata to identify:

- participant/session
- scenario
- trial
- timestamps
- experimental condition
- behavioural measurements
- relevant vehicle information
- system events

The exact schema should be kept stable once the evaluation begins.

Avoid changing the data format casually after pilot data has been collected.

A useful conceptual structure is:

### Session metadata

- session ID
- participant ID/code
- date/time
- application version
- scenario configuration/version

### Scenario metadata

- scenario ID
- traffic condition
- speed condition
- random seed if applicable

### Event data

- timestamp
- event type
- participant state
- relevant vehicle ID
- position/distance information

### Time-series data

At a configurable sampling rate:

- timestamp
- participant position
- participant rotation
- participant velocity
- relevant vehicle positions
- relevant vehicle velocities

Do not record at an unnecessarily high frequency if it creates performance problems or produces unusably large datasets.

---

# 14. Reproducibility

Research scenarios should be repeatable.

Where possible:

- use explicit scenario configuration
- use deterministic spawning
- use configurable random seeds
- record seeds used
- record application/build version
- record scenario version
- record relevant configuration values in exported data

The goal is that a researcher can later understand exactly which conditions produced a dataset.

---

# 15. Researcher controls

The application should have a researcher/developer configuration mechanism rather than requiring source-code changes for every experiment.

Useful controls may include:

- scenario selection
- traffic density
- traffic speed
- random seed
- scenario duration
- start/reset scenario
- participant reset
- logging enable/disable
- debug information
- data export

The researcher UI should not interfere with the participant experience.

It may be appropriate to have:

1. a participant-facing VR experience
2. a researcher/developer configuration interface

Do not expose debug controls to participants during the actual evaluation unless deliberately required.

---

# 16. Participant-facing UI

Keep the participant experience simple.

Potential participant UI:

- welcome/instructions
- task instructions
- scenario ready state
- brief completion indication

Avoid unnecessary:

- scores
- game-like rewards
- leaderboards
- distracting HUD elements
- excessive menus

The participant's attention should remain on the pedestrian task.

---

# 17. Evaluation

The intended practical evaluation is a small formative usability evaluation with approximately four participants.

The purpose is to identify whether the platform:

- can be used successfully
- provides an understandable pedestrian crossing task
- has major usability problems
- provides sufficient perceived realism for the intended research purpose
- causes unacceptable simulator discomfort
- successfully records the intended behavioural data
- provides a workable experimental procedure
- needs changes before larger-scale research

---

# 18. Questionnaires

The project has questionnaire material covering areas such as:

- demographics
- perceived realism
- usability
- perceived safety
- simulator experience/sickness
- post-study feedback

Questionnaire design should be aligned with the revised research questions.

Do not implement questionnaire questions simply because they existed in an earlier study design.

The questionnaires should support the research objectives and the small formative evaluation.

With approximately four participants:

- report responses descriptively
- avoid population-level claims
- avoid treating the sample as statistically representative
- use participant feedback to identify usability and feasibility issues
- use behavioural data as exploratory evidence

---

# 19. Ethics

The application involves human participants.

The project must follow the University's applicable human research ethics requirements.

Important:

- A change from a larger planned participant study to approximately four participants does not automatically remove the need to consider ethics approval.
- Changes to study design, participants, procedures, questionnaires, or other approved materials may require an ethics amendment.
- Do not recruit participants or conduct the evaluation until the supervisor/research team has confirmed that the study is covered by the appropriate ethics approval/procedure.

Claude Code should not make assumptions about ethics approval.

If implementation changes would materially alter the approved participant procedure, flag the change for the research team rather than silently treating it as approved.

---

# 20. Unity and development environment

The project is being developed in Unity.

The currently planned Unity version is:

**Unity 6000.3.5f2**

However, always inspect the repository's actual Unity project version and package configuration before making changes.

The project uses VR/XR functionality.

Before coding:

1. inspect `ProjectSettings`
2. inspect `Packages/manifest.json`
3. inspect `Packages/packages-lock.json`
4. identify the XR stack actually being used
5. inspect existing scenes
6. inspect existing prefabs
7. inspect existing scripts
8. inspect assembly definitions if present
9. understand existing input configuration
10. identify the current build target

Do not install or replace major XR packages without first understanding the existing setup.

---

# 21. Architecture principles

Prefer a modular architecture with clear responsibilities.

A conceptual architecture is:

```text
Researcher Configuration
        |
        v
Scenario Manager
        |
        +------------------+
        |                  |
        v                  v
Traffic Manager     Participant Manager
        |                  |
        +--------+---------+
                 |
                 v
          Behaviour/Event System
                 |
                 v
           Logging Manager
                 |
                 v
             Data Export
```

This is a conceptual model, not a requirement to create exactly these classes.

Follow the repository's existing architecture where it already provides suitable abstractions.

---

# 22. Suggested major systems

## 22.1 Scenario Manager

Responsible for:

- loading scenario configuration
- starting a scenario
- resetting a scenario
- exposing current experimental conditions
- coordinating scenario lifecycle
- notifying other systems when a scenario begins/ends

It should not contain all traffic logic.

---

## 22.2 Traffic Manager

Responsible for:

- spawning vehicles
- controlling vehicle movement
- applying traffic conditions
- tracking active vehicles
- managing vehicle behaviour
- exposing relevant traffic state to instrumentation

Keep traffic logic separate from logging.

---

## 22.3 Participant Manager

Responsible for:

- participant state
- movement/task state
- crossing state
- reset
- task completion
- relevant participant events

Do not couple participant logic unnecessarily to specific traffic implementations.

---

## 22.4 Measurement / Behaviour System

Responsible for deriving meaningful research measurements from participant and traffic state.

Examples:

- waiting time
- crossing duration
- gap size
- closest vehicle distance
- relative speed
- time-to-collision where appropriate
- head movement
- crossing outcome

Metrics should have clear definitions.

For every metric, be able to answer:

1. What exactly does it measure?
2. When does measurement begin?
3. When does it end?
4. What data does it depend on?
5. What assumptions does it make?
6. How will it be exported?

---

## 22.5 Logging Manager

Responsible for:

- session metadata
- event logging
- time-series logging
- scenario metadata
- file creation
- CSV export
- safe shutdown/finalisation

Do not spread file-writing logic throughout the application.

---

# 23. Suggested data flow

A typical trial should look conceptually like:

```text
Load scenario
    |
    v
Initialise environment
    |
    v
Initialise traffic
    |
    v
Start logging
    |
    v
Participant begins task
    |
    v
Collect participant + traffic state
    |
    v
Detect research events
    |
    v
Participant completes crossing
    |
    v
Complete scenario
    |
    v
Finalise trial data
    |
    v
Export/store data
```

The implementation should keep this lifecycle explicit.

---

# 24. Performance requirements

VR performance matters.

Prioritise:

- stable frame rate
- low latency
- efficient vehicle simulation
- sensible physics usage
- reasonable draw calls
- efficient logging
- limited unnecessary allocations
- avoiding expensive per-frame operations where event-based logic is sufficient

Do not sacrifice VR usability for unnecessary instrumentation detail.

If high-frequency logging causes performance issues, consider:

- lower sampling frequency
- configurable sampling
- selective logging
- buffering
- separate event and time-series logs

---

# 25. Development priorities

Prioritise implementation in roughly this order:

### Phase 1 — Repository understanding

Before modifying code:

- inspect the project
- identify existing systems
- run the project
- identify current scene
- identify VR setup
- understand input
- identify existing environment work
- identify current scripts and prefabs

### Phase 2 — Stable VR foundation

Ensure:

- application launches
- headset works
- participant can move
- scale is correct
- environment loads reliably
- reset works

### Phase 3 — Environment

Implement/refine:

- road
- footpaths
- crossing
- surrounding buildings/environment
- signs/markings
- relevant visual details

Prioritise the experimental area.

### Phase 4 — Traffic

Implement:

- vehicle spawning
- movement
- configurable density
- configurable speed
- repeatable scenarios
- safe reset

### Phase 5 — Scenario system

Implement:

- scenario data
- scenario loading
- scenario lifecycle
- researcher configuration

### Phase 6 — Behavioural instrumentation

Implement and test:

- participant tracking
- crossing detection
- waiting time
- crossing duration
- vehicle distance/speed
- gap-related measurements
- relevant head orientation data
- event detection

### Phase 7 — Logging/export

Implement:

- session metadata
- scenario metadata
- event logs
- time-series logs
- CSV export
- reliable file handling

### Phase 8 — Usability/evaluation preparation

Implement:

- participant instructions
- scenario reset
- consistent procedure
- researcher controls
- test mode
- debug tools
- stable build

### Phase 9 — Testing

Test:

- every scenario
- reset behaviour
- data export
- event timing
- participant movement
- vehicle movement
- edge cases
- VR performance
- repeated runs

---

# 26. Testing expectations

Research software needs more than a single successful playthrough.

At minimum, test:

## Scenario tests

- each scenario loads
- correct traffic conditions are applied
- scenario resets correctly
- scenario ends correctly

## Traffic tests

- vehicles spawn correctly
- vehicles follow intended paths
- speed settings are applied
- density settings are applied
- vehicles do not become permanently stuck
- traffic can be reset

## Participant tests

- participant starts in the correct location
- movement works
- crossing detection works
- task completion works
- reset works

## Measurement tests

Verify known situations produce expected measurements.

For example:

- known crossing start/end times produce the expected duration
- known participant/vehicle positions produce the expected distance
- event timestamps occur in the correct order
- scenario IDs are correctly attached to measurements

## Logging tests

Verify:

- files are created
- headers are correct
- session IDs are unique
- scenario information is included
- timestamps are sensible
- data is not silently lost
- files are finalised after a run

---

# 27. Research validity principles

The implementation must support the distinction between:

### What the system can demonstrate

- a controlled VR environment can be constructed
- scenarios can be configured
- participants can interact with the environment
- behavioural data can be captured
- measurements can be exported
- the experimental procedure can be tested
- usability issues can be identified

### What approximately four participants cannot establish

- population-level pedestrian behaviour
- robust statistical generalisation
- definitive causal effects of traffic conditions
- real-world behavioural validity
- broad conclusions about pedestrian safety

Do not accidentally introduce analysis or UI features that imply stronger conclusions than the study design supports.

---

# 28. What not to build unless explicitly requested

Avoid unnecessary scope expansion.

Do not independently add:

- city-scale digital twin functionality
- live traffic feeds
- live Google/Mapbox traffic
- machine-learning pedestrian models
- advanced autonomous driving systems
- complex weather simulation
- dynamic seasons
- multiplayer
- online services
- cloud infrastructure
- unnecessary database systems
- elaborate game mechanics
- scoring/leaderboards
- unnecessary networking
- highly detailed vehicle interiors
- complex NPC pedestrian populations unless required by the research design

A feature should have a clear research or engineering justification before being added.

---

# 29. Coding principles

## Keep systems focused

Prefer classes/components with clear responsibilities.

Avoid giant manager classes containing:

- traffic
- participant movement
- UI
- logging
- scenario configuration
- measurement

all at once.

## Prefer configuration over hard-coding

Experimental parameters should live in appropriate configuration objects.

## Keep research definitions explicit

Do not hide important research calculations inside obscure helper methods.

For example, the definition of a crossing duration should be easy to find and understand.

## Avoid unnecessary abstraction

Do not create elaborate frameworks for simple functionality.

The project has limited time and a research deliverable.

Use the simplest architecture that remains maintainable and testable.

## Preserve existing work

Before replacing a system:

1. understand why it exists
2. inspect how it is used
3. determine whether it already satisfies the requirement
4. modify it incrementally where possible

---

# 30. Git and change discipline

When making changes:

- keep changes focused
- avoid unrelated refactoring
- do not modify project settings without understanding the consequences
- do not change packages casually
- do not delete existing assets/scripts without checking references
- avoid committing generated build artefacts
- keep commits logically separable where practical

When a change affects research methodology, measurement definitions, participant procedure, or data format, explicitly flag it.

---

# 31. Claude Code workflow

For each substantial task:

### Step 1 — Understand

Inspect the relevant existing code and assets.

### Step 2 — Identify dependencies

Determine:

- what existing systems are involved
- what scenes/prefabs are affected
- what packages are involved
- what data is produced
- whether research measurements are affected

### Step 3 — Plan

Before making a substantial architectural change, outline:

- files/components affected
- intended behaviour
- testing approach
- research implications

### Step 4 — Implement

Make the smallest coherent change that satisfies the requirement.

### Step 5 — Test

Run the relevant Unity tests or manual test procedure.

### Step 6 — Review

Check:

- performance
- research instrumentation
- data output
- regressions
- maintainability

### Step 7 — Report

Summarise:

- what changed
- files changed
- how it works
- tests performed
- any remaining issues
- any research implications

---

# 32. When to stop and ask the team

Do not silently make decisions that materially change the research design.

Flag issues such as:

- changing what participants are asked to do
- adding/removing participant measurements
- changing questionnaire content
- changing the planned experimental conditions
- changing the definition of a research metric
- changing the data schema after data collection begins
- introducing a new participant interaction
- materially changing the VR environment used for evaluation
- changes that may affect ethics approval
- changes that significantly expand project scope

For ordinary implementation choices, use reasonable engineering judgement.

---

# 33. Definition of done

A feature should generally be considered complete when:

1. It satisfies the intended requirement.
2. It integrates with the existing architecture.
3. It does not unnecessarily duplicate functionality.
4. It works in the actual Unity scene.
5. It has been tested.
6. It does not introduce obvious VR performance problems.
7. Relevant research data is logged correctly.
8. The data is associated with the correct scenario/session.
9. The implementation is understandable to another developer.
10. Any research-design implications have been identified.

---

# 34. Overall success criteria

The project application should ultimately allow the research team to:

1. Launch the VR environment.
2. Place a participant in the Symonds Street environment.
3. Select a controlled traffic scenario.
4. Start a repeatable trial.
5. Have the participant approach and cross the road.
6. Simulate controlled traffic conditions.
7. Capture relevant participant and traffic behaviour.
8. Detect important crossing events.
9. Store structured research data.
10. Repeat the procedure reliably.
11. Export data for analysis.
12. Conduct a small formative usability evaluation.
13. Identify technical/usability limitations.
14. Use the resulting evidence to assess whether the platform is suitable for future larger pedestrian safety research.

---

# 35. Final guiding principle

The application is a **research instrument first and a visual demonstration second**.

When deciding between implementation options, favour the option that:

- supports the research questions,
- produces reliable and interpretable data,
- makes experimental conditions controllable,
- is reproducible,
- is understandable and maintainable,
- performs reliably in VR,
- and avoids unnecessary scope.

At the same time, do not overengineer the system merely because a technically sophisticated solution is possible.

The goal is a **credible, usable, reproducible VR research platform that can be evaluated within the available project time and participant sample**.
