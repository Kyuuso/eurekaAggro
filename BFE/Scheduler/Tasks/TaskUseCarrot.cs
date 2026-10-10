namespace BFE.Scheduler.Tasks
{
    internal static class TaskUseCarrot
    {
        public static void Enqueue()
        {
            uint carrotItemId = CarrotKeyItem;

            P.taskManager.Enqueue(PlayerNotBusy);
            P.taskManager.Enqueue(() => RunCommand("e Using bunny carrot"));
            P.taskManager.Enqueue(() => UseInventoryContextItem(carrotItemId), "Use Bunny Carrot");
        }
    }
}
