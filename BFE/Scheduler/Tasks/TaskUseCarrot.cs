namespace BFE.Scheduler.Tasks
{
    internal static class TaskUseCarrot
    {
        public static void Enqueue()
        {
            uint carrotItemId = CarrotKeyItem;

            P.taskManager.Enqueue(PlayerNotBusy);
            P.taskManager.Enqueue(() => RunCommand("e Using bunny carrot"));
            // Drop the previous carrot hint so CheckDirections only matches the toast from this use
            P.taskManager.Enqueue(() => P.filter.ClearLastToast());
            P.taskManager.Enqueue(() => UseInventoryContextItem(carrotItemId), "Use Bunny Carrot");
        }
    }
}
