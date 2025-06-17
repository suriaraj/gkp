// Assuming this is inside a loop or function where `entity` is available

if (ritmWithTask != null && ritmWithTask.RitmResponse?.result?.Length > 0)
{
    var ritm = ritmWithTask.RitmResponse.result[0];
    entity.RITM = ritm.number;
    entity.RITMStatus = ritm.GetStatus();
    entity.SCTaskSysId = ritmWithTask.SCTaskSysId; // <- Store SC Task sys_id in entity
    await inProgressService.UpdateAsync(entity);   // <- Update in DB if needed
}