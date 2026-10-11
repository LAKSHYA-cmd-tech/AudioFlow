#pragma once

// A delayed callback reports the latest completed packet. Missed periods must
// not become repeated notifications for that same packet on subsequent ticks.
inline unsigned long long AudioFlowNotificationRemainder(
    unsigned long long elapsedHns, unsigned long long intervalHns)
{
    return intervalHns ? elapsedHns % intervalHns : 0;
}
