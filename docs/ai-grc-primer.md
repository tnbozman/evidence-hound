# AI governance for a cyber GRC team

This primer is for a governance, risk and compliance (GRC) team that knows cyber compliance but is new to AI governance. It takes about 15 minutes to read. It explains what AI governance asks of a small business, using the Australian Government's *Guidance for AI Adoption* (known as AI6), and shows each step with the fictional Example Pty Ltd.

You do not need to become an AI expert. Most of the work is the same work you already do for cyber: know what is in use, who owns it, what could go wrong, what was checked, and who can step in.

## AI governance in plain words

**AI GRC** is governance, risk and compliance applied to the AI tools your organisation uses or builds. For each one you want to be able to answer: what is it, who is responsible, what could go wrong, what did we check before relying on it, and who can step in when it gets something wrong.

**An AI system** is any tool that makes predictions, recommendations, content or decisions from data with some autonomy. Examples are a chat assistant staff use, an AI feature switched on inside a software-as-a-service product, or a model a developer built. For a small business most AI systems are third-party products, so most of the work is knowing they are there and who looks after them.

**AI6** is voluntary guidance from the National AI Centre (October 2025). It is not a law or a certification. It does for AI what the Essential Eight does for cyber: a short list of practices to adopt and show you follow. There is no AI6 audit body. The AI6 audit in CISO Assistant is your own self-assessment, for the security committee and the board.

AI6 comes in two versions with the same six practices:

- **Foundations** is for organisations starting out, or using AI in low-risk ways. It has the six practices, each with "Getting started" and "Next steps" actions. Start here.
- **Implementation** is for organisations that build or customise AI, or use it for higher-risk decisions. It splits the practices into 21 sub-practices with detailed actions.

The example uses Foundations. In CISO Assistant the AI6 framework has both, as two groups: `F` for Foundations and `I` for Implementation. An audit for group `F` shows only the rows Foundations asks for.

## How the work fits the tools you already use

- **CISO Assistant stays the system of record.** Each AI system is an asset of the class *AI system*, with an owner and a few extra fields: its risk tier, how people oversee it, when it was screened, assessed and tested. AI risks are risk scenarios linked to the asset. A third-party AI product is a solution of a supplier, as for any other supplier.
- **The register is CISO Assistant's asset list**, filtered to AI systems. You export it with CISO Assistant's own Excel export. There is no separate spreadsheet.
- **Your repo holds what you decide**: which AI systems exist, who owns them, their tier and oversight arrangement. Changes go through pull requests like the rest of your scope.
- **hound checks the register on every run**: a system with no owner, a medium or high tier system with no impact assessment, a test date that has gone stale. Those checks fill the AI6 rows that can be checked; the rest are manual evidence with an owner and a reminder, as for the Essential Eight.

## The six practices, with Example Pty Ltd

Example Pty Ltd has twelve staff. Its IT team is Alex Example, and Sam Example is the CEO. The GRC team is Alex, two to eight hours a week.

### 1. Decide who is accountable

Name one senior person accountable for AI overall, and an owner for each AI system. Make sure staff who use AI know enough to use it well, and that suppliers' responsibilities are clear.

**Example Pty Ltd:** Sam (the CEO) is accountable for AI. Staff start using a chat assistant, Example Chat, to draft emails and proposals. Alex registers it as an AI system in the repo with Alex as its owner, and adds a one-page AI policy to the documents of the AI6 audit. Sam approves the policy. A ten-minute briefing at the staff meeting covers what not to paste into the assistant.

### 2. Understand impacts and plan accordingly

Before using an AI system, think about who it affects and how. Give people a way to raise a concern or ask for a decision to be reviewed by a person.

**Example Pty Ltd:** Example Chat drafts text that a staff member always reads before sending, so it affects customers only through what staff choose to send. Alex records that in a short impact note on the asset. The existing complaints address on the website is the feedback route; Alex adds a line saying a person reviews any complaint about AI-drafted content.

### 3. Measure and manage risks

Screen each AI system with a short set of questions to sort it into a risk tier: low, medium, high or prohibited. Record the risks of medium and high tier systems, and the controls for them.

**Example Pty Ltd:** Alex answers the screening questions for Example Chat. It makes no decisions about people, handles no sensitive personal data by design, and its output is always checked, so it is **low** tier. A few months later, the sales team asks to switch on an AI feature in the customer system that scores leads. That one ranks people, so screening puts it at **medium**: Alex records a risk scenario in CISO Assistant ("leads from some regions are scored unfairly low") with its control (a monthly spot check of low-scored leads), and an impact assessment is done before it goes live.

### 4. Share essential information

Keep a register of AI systems. Tell people when they are dealing with AI or with AI-generated content, and know what your suppliers tell you about their AI.

**Example Pty Ltd:** the register is the list of *AI system* assets in CISO Assistant, two entries by now. The proposal template gains a footer line saying drafts may be prepared with AI assistance and are reviewed by staff. The supplier of the customer system is already a third party in CISO Assistant; its assessment now includes a question about its AI feature.

### 5. Test and monitor

Check that an AI system works as intended before relying on it, and keep checking. The riskier the system, the more often.

**Example Pty Ltd:** before switching on lead scoring, the sales lead compares its scores with last quarter's actual sales for 50 leads and records the result as evidence; Alex sets the test date on the asset. hound checks that date on every run: a medium tier system tested more than 12 months ago (6 for high tier) shows as a failing check, and the AI6 row for testing follows it.

### 6. Maintain human control

Make sure a person can oversee, override or switch off the system, and know how you would stop using it.

**Example Pty Ltd:** Example Chat is *human in the loop*: a person approves every output before it is used. Lead scoring is *human on the loop*: it runs by itself, and the sales lead watches the monthly spot check and can switch the feature off in the customer system's settings. Alex writes both arrangements in the oversight note on each asset. hound fails the oversight check if a high tier system has nobody in or on the loop.

## What the committee sees

The monthly security committee report gains an AI section: how many AI systems there are by tier, which were added or retired this month, which fail a check, and the AI6 rows that are not yet met. The quarterly board report shows the AI6 self-assessment beside the Essential Eight. For Example Pty Ltd that is a few lines: two systems (one low, one medium), nothing failing, and the next step on the plan.

## What you do not need to do at Foundations

- You do not need the 21 sub-practices or their detailed actions. Those are Implementation, for organisations that build AI or use it for higher-risk decisions.
- You do not need to test or audit a supplier's model. For a third-party product, check what the supplier tells you and how you use it.
- You do not need a separate AI risk method. Use your existing risk register, with AI risks as risk scenarios.
- You do not need an impact assessment for a low tier system. Screening is enough, and you redo it when the use changes.
- You do not need a certificate or an external audit. AI6 is a self-assessment.
- You do not need a separate register spreadsheet. CISO Assistant's asset list is the register.

## Glossary

| Term | Meaning |
| --- | --- |
| AI system | A tool that makes predictions, recommendations, content or decisions from data with some autonomy. |
| AI6 | The six practices of the Australian Government's *Guidance for AI Adoption* (National AI Centre, 2025). |
| Foundations | The short version of AI6, for organisations starting out or using AI in low-risk ways. |
| Implementation | The detailed version of AI6, with 21 sub-practices, for organisations that build AI or use it for higher-risk decisions. |
| Screening | A short questionnaire that sorts an AI system into a risk tier. |
| Risk tier | Low, medium, high or prohibited. A prohibited system is not used. |
| Impact assessment | A fuller review of who a medium or high tier system affects and how, done before it goes live. |
| Human in the loop | A person approves each output before it is used. |
| Human on the loop | The system acts by itself while a person watches and can step in. |
| Human out of the loop | Nobody reviews the system's outputs as it works; only afterwards, if at all. |
| Register | The list of AI systems, kept in CISO Assistant as assets of the class *AI system*. |
| Third-party AI | An AI product or feature you use but did not build, recorded with its supplier. |

## Where to go next

- The walkthrough in `docs/tutorials/ai-governance.md` registers an AI system on the local stack and follows it through screening, assessment, testing and the committee report. It is written once the AI register and its checks are built (sprint s11).
- The guidance itself: *Guidance for AI Adoption: Foundations* and *Implementation guidance*, on ai.gov.au.
