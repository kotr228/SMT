using DocControlService.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocControlAI.Services
{
    /// <summary>
    /// Сервіс для реорганізації файлів
    /// </summary>
    public class FileReorganizationService
    {
        public List<FileReorganizationAction> PreviewActions(
            List<StructureViolation> violations)
        {
            var actions = new List<FileReorganizationAction>();

            foreach (var violation in violations)
            {
                actions.Add(new FileReorganizationAction
                {
                    SourcePath = violation.FilePath,
                    DestinationPath = violation.SuggestedPath,
                    ActionType = ReorganizationActionType.Move,
                    Reason = violation.Description
                });
            }

            return actions;
        }

        public bool ApplyReorganization(
            List<FileReorganizationAction> actions,
            bool createBackup = true)
        {
            try
            {
                foreach (var action in actions)
                {
                    // Створюємо backup якщо потрібно
                    if (createBackup)
                    {
                        string backupPath = action.SourcePath + ".backup";
                        File.Copy(action.SourcePath, backupPath, true);
                    }

                    // Створюємо директорію якщо не існує
                    string destDir = Path.GetDirectoryName(action.DestinationPath);
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Переміщуємо файл
                    File.Move(action.SourcePath, action.DestinationPath, true);

                    Console.WriteLine($"✅ Перемістив: {Path.GetFileName(action.SourcePath)}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка реорганізації: {ex.Message}");
                return false;
            }
        }
    }
}