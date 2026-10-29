# GSM Hardware Validation Plan and Record

## Status

**No physical GSM modem, SIM, detector, microcontroller, or Nigerian mobile network was available
during this audit. No hardware result is claimed as passed.**

The physical adapter is implemented and tested against a scripted serial transport. That is software
evidence only. A scripted transport does not prove USB-driver behavior, modem firmware compatibility,
SIM provisioning, radio coverage, SMS routing, antenna performance, or detector operation.

## System boundary

```text
Gas sensor -> signal conditioning -> local alarm / relay (independent)
                                  -> microcontroller -> GSM transmitter + detector SIM
                                  -> cellular SMS network
                                  -> USB GSM modem + receiver SIM
                                  -> Windows serial port
                                  -> GasGuard Desktop (monitoring / evidence only)
```

The desktop app must never be the only alarm path. The detector's local audible/visual alarm and any
shutdown/interlock must function with the PC powered off, the USB cable removed, the database
unavailable, the modem removed, SMS failed, and the internet unavailable. No software test in this
repository proves that hardware property.

## Modem compatibility assumptions to verify

Do not assume a USB "GSM dongle" exposes the required serial AT port. Verify the exact model and
firmware manual for:

- a Windows USB serial COM port (`System.IO.Ports`);
- text mode `AT+CMGF=1`;
- SIM status `AT+CPIN?`;
- signal `AT+CSQ`;
- network registration `AT+CREG?`;
- operator `AT+COPS?`;
- storage listing `AT+CMGL="REC UNREAD"`;
- deletion `AT+CMGD=<index>`;
- send `AT+CMGS="+234..."` with prompt and Ctrl-Z;
- new-message notification `AT+CNMI=2,1,0,0,0` or a documented equivalent.

The current adapter uses text mode and accepts single-line printable ASCII only. PDU-only modems,
multipart SMS, UCS-2 and delivery receipts are not supported. A modem that cannot expose unread stored
messages must not be treated as production-ready.

## Commands implemented (subject to modem documentation)

Connection/status:

```text
ATE0
AT
ATI
AT+CPIN?
AT+CSQ
AT+CREG?
AT+COPS?
AT+CMGF=1
AT+CSCS="GSM"     (fallback: AT+CSCS="IRA")
AT+CNMI=2,1,0,0,0
```

Reception/reconciliation and deletion:

```text
AT+CMGL="REC UNREAD"
AT+CMGD=<storage index>
```

Sending a confirmed test/automatic message:

```text
AT+CMGS="+2348031234567"
> <printable ASCII message><Ctrl-Z>
```

The adapter serializes commands, handles `OK`, `ERROR`, `+CME ERROR`, `+CMS ERROR`, prompt/payload,
timeout, cancellation, unsolicited `+CMT`, `+CMTI`, echo and transport faults. A command timeout or
pipeline desynchronization marks the session faulted; reconnect is required.

## SIM preparation

1. Confirm the SIM works in a normal phone and can send/receive SMS on the target Nigerian network.
2. Record the detector SIM number; enter the same number as the detector's expected sender in
   GasGuard.
3. Disable or unlock the SIM PIN using a phone/approved process. GasGuard does not store or enter SIM
   PINs.
4. Confirm SMS service, SMSC provisioning, airtime/plan, and coverage.
5. Do not write PINs, APNs or credentials into source, logs, screenshots or this repository.

## Required validation record

Fill this table only with observed evidence. Record modem model, USB driver version, firmware, SIM
operator/number (redact the last digits if reports are shared), Windows build, antenna, location, and
date.

| # | Test | Method | Expected evidence | Result |
|---|---|---|---|---|
| 1 | Detector power and local alarm | Qualified hardware test per manufacturer; PC and modem disconnected | Local audible/visual alarm operates with PC off | **NOT PERFORMED** |
| 2 | Sensor warm-up/calibration | Manufacturer-approved calibration procedure only; no uncontrolled gas | Documented warm-up and calibration record; raw value not represented as certified ppm | **NOT PERFORMED** |
| 3 | Detector-to-GSM message generation | Trigger approved device self-test/fixture; capture exact SMS text | Exact ASCII v1 message with correct device/event/level/reading/seq | **NOT PERFORMED** |
| 4 | USB modem/SIM operation | Install driver, identify AT COM port, connect in GasGuard | ATI, CPIN READY, CREG registered, CSQ valid, CMGF/CNMI status visible | **NOT PERFORMED** |
| 5 | Nigerian network/SMS delivery | Send between detector SIM and modem SIM on each intended operator | SMS received; sender/body match; no false delivery claim | **NOT PERFORMED** |
| 6 | Low signal/network outage | Controlled antenna/network-loss test per operator procedure | Modem reports weak/not registered; no detector-online claim; recovery after network returns | **NOT PERFORMED** |
| 7 | Power loss/recovery | Remove detector/USB/PC power in a controlled sequence | Local alarm remains independent; app journal/DB recovers; no duplicate incident | **NOT PERFORMED** |
| 8 | Detector disconnect/stale indication | Disconnect detector or stop messages; wait beyond stale threshold | App shows OFFLINE/STALE, last-report age, not false NORMAL | **NOT PERFORMED** |
| 9 | Duplicate and delayed SMS | Retain/resend same SMS; delay delivery; reset detector sequence | Duplicate retained/suppressed without duplicate incident; critical after sequence reset not lost | **NOT PERFORMED** |
| 10 | Local alarm with PC off | Repeat safe approved local alarm test with Windows shut down | Alarm remains operational independent of GasGuard/PC/SMS | **NOT PERFORMED** |

Additional recommended checks:

- SIM PIN-required state is displayed and the app does not claim SMS readiness.
- Modem unplug/replug produces a visible fault and a controlled reconnect.
- SMS storage deletion occurs only after DB persistence; a forced DB failure leaves the message in
  storage for reconciliation.
- The selected baud/parity/stop bits match the modem; text mode works after cold boot.
- SMS sender formats with/without `+234` map to the expected detector.
- Any modem-specific `AT+CNMI`/storage behavior is recorded.

## Safety limits

- Do not use uncontrolled gas, naked flames, or unapproved test fixtures.
- Do not use an MQ-5/educational gas sensor as a certified life-safety instrument.
- Do not implement automatic gas-valve shutoff through this desktop app without a separate,
  professionally engineered, risk-assessed local safety system with rated hardware.
- Raw ADC values are not ppm/LEL and must not be used as clearance thresholds.
- A connected modem or a received SMS is not proof that any physical area is safe.
- SMS sender identity is not cryptographic; see
  [SMS_SECURITY_AND_PROTOCOL.md](SMS_SECURITY_AND_PROTOCOL.md).

## Acceptance for hardware validation

Do not call the hardware integration validated until all applicable table rows have actual evidence
from the intended modem, SIM, network, detector and local alarm, signed/dated by the test operator.
Until then, the correct status is: **software-verified, hardware-unvalidated**.
