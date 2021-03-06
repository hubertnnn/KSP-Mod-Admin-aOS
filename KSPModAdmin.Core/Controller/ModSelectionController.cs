﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using KSPModAdmin.Core.Model;
using KSPModAdmin.Core.Utils;
using KSPModAdmin.Core.Utils.Controls.Aga.Controls.Tree;
using KSPModAdmin.Core.Utils.Localization;
using KSPModAdmin.Core.Utils.Logging;
using KSPModAdmin.Core.Views;

namespace KSPModAdmin.Core.Controller
{
    public class ModSelectionController
    {
        #region Member variables

        /// <summary>
        /// List of known mods.
        /// </summary>
        private static ModSelectionTreeModel mModel = new ModSelectionTreeModel();

        #endregion

        #region Properties

        /// <summary>
        /// Gets the singleton of this class.
        /// </summary>
        protected static ModSelectionController Instance { get { return mInstance ?? (mInstance = new ModSelectionController()); } }
        protected static ModSelectionController mInstance = null;

        /// <summary>
        /// Gets or sets the view of the controller.
        /// </summary>
        public static ucModSelection View { get; protected set; }

        /// <summary>
        /// Gets the list of all known mods of the ModSelection.
        /// </summary>
        public static ModSelectionTreeModel Model
        {
            get { return mModel; } 
        }

        /// <summary>
        /// Gets all mods of the ModSelection.
        /// </summary>
        public static ModNode[] Mods { get { return Model.Nodes.Cast<ModNode>().ToArray(); } }

        #endregion

        #region Constructors

        /// <summary>
        /// Private constructor (use static function only).
        /// </summary>
        private ModSelectionController()
        {
        }

        /// <summary>
        /// Static constructor. Creates a singleton of this class.
        /// </summary>
        static ModSelectionController()
        {
            if (mInstance == null)
                mInstance = new ModSelectionController();
        }

        #endregion


        /// <summary>
        /// This method gets called when your Controller should be initialized.
        /// Perform additional initialization of your UserControl here.
        /// </summary>
        internal static void Initialize(ucModSelection view)
        {
            View = view;

            EventDistributor.AsyncTaskStarted += AsyncTaskStarted;
            EventDistributor.AsyncTaskDone += AsyncTaskDone;
            EventDistributor.LanguageChanged += LanguageChanged;
            EventDistributor.KSPRootChanged += KSPRootChanged;

            ModSelectionTreeModel.BeforeCheckedChange += BeforeCheckedChange;
        }


        #region Event callback functions

        #region EventDistributor callback functions.

        /// <summary>
        /// Callback function for the AsyncTaskStarted event.
        /// Should disable all controls of the BaseView.
        /// </summary>
        protected static void AsyncTaskStarted(object sender)
        {
            View.SetEnabledOfAllControls(false);
        }

        /// <summary>
        /// Callback function for the AsyncTaskDone event.
        /// Should enable all controls of the BaseView.
        /// </summary>
        protected static void AsyncTaskDone(object sender)
        {
            View.SetEnabledOfAllControls(true);
        }

        /// <summary>
        /// Callback function for the LanguageChanged event.
        /// Translates all controls of the BaseView.
        /// </summary>
        protected static void LanguageChanged(object sender)
        {
            // translates the controls of the view.
            ControlTranslator.TranslateControls(Localizer.GlobalInstance, View as Control, OptionsController.SelectedLanguage);
        }

        /// <summary>
        /// Callback of the OptionsController for known KSP install path changes.
        /// </summary>
        /// <param name="kspPath">The new KSP install paths.</param>
        protected static void KSPRootChanged(string kspPath)
        {
            // MainController loads the new KSPMAMod.cfg and populates the TreeView.
            //View.tvModSelection.SelectedNode = null;
        }

        #endregion

        /// <summary>
        /// Callback of the ModSelectionTreeModle when a checked state of a ModNode is changing.
        /// </summary>
        /// <param name="sender">Invoker of the BeforeCheckedChange event.</param>
        /// <param name="args">The BeforeCheckedChangeEventArgs.</param>
        /// <returns>True if the change should be continued, otherwise false.</returns>
        protected static void BeforeCheckedChange(object sender, BeforeCheckedChangeEventArgs args)
        {
            if (args.Node == null)
                return;

            if (!args.Node.ZipExists)
            {
                if (!args.NewValue)
                    args.Cancel = (DialogResult.Yes != MessageBox.Show(View.ParentForm, Messages.MSG_UNCHECK_NO_ZIPARCHIVE_WARNING, Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.YesNo));
                else
                {
                    if (args.Node.IsInstalled)
                        return;
                    
                    MessageBox.Show(View.ParentForm, Messages.MSG_CHECK_NO_ZIPARCHIVE_WARNING, Messages.MSG_TITLE_ATTENTION);
                    args.Cancel = true;
                }
            }
            else if (args.NewValue)
            {
                if (!args.Node.HasDestination || args.Node.HasChildesWithoutDestination)
                {
                    string msg = string.Format(Messages.MSG_0_HAS_CHILDES_WITHOUT_DESTINATION_WARNING, args.Node.Name);
                    MessageBox.Show(View.ParentForm, msg, Messages.MSG_TITLE_ATTENTION);
                    if (args.Node.IsFile || (!args.Node.IsFile && !args.Node.HasDestinationForChilds))
                        args.NewValue = false;
                }
            }
        }

        #endregion


        /// <summary>
        /// Forces the view to redraw.
        /// </summary>
        public static void InvalidateView()
        {
            View.InvalidateView();
        }

        #region Add Mod

        /// <summary>
        /// Opens the add dialog to add mods via CurseForge, KSP Forum or path.
        /// </summary>
        public static void OpenAddModDialog()
        {
            frmAddMod dlg = new frmAddMod();
            dlg.ShowDialog();
            View.InvokeIfRequired(() => { InvalidateView(); });
        }

        /// <summary>
        /// Opens a OpenFileDialog to add mods path.
        /// </summary>
        public static void OpenAddModFileDialog()
        {
            OpenFileDialog dlg = new OpenFileDialog { Multiselect = true, Filter = Constants.ADD_DLG_FILTER };
            if (dlg.ShowDialog() == DialogResult.OK)
                AddModsAsync(dlg.FileNames);
            else
                Messenger.AddInfo(Messages.MSG_ADDING_MOD_FAILED);
        }

        /// <summary>
        /// Adds a mod from HD with given ModInfos.
        /// </summary>
        /// <param name="modInfo">The ModInfos of the mod to add.</param>
        /// <param name="installAfterAdd">Flag that determines if the mod should be installed after adding to the ModSelection.</param>
        /// <returns>The new added mod (maybe null).</returns>
        public static ModNode HandleModAddViaModInfo(ModInfo modInfo, bool installAfterAdd)
        {
            ModNode newMod = null;
            List<ModNode> addedMods = AddMods(new ModInfo[] { modInfo }, true, null);
            if (addedMods.Count > 0 && !string.IsNullOrEmpty(modInfo.Name))
                addedMods[0].Text = modInfo.Name;

            if (installAfterAdd)
                ProcessMods(addedMods.ToArray());

            if (addedMods.Count > 0)
                newMod = addedMods[0];

            return newMod;
        }

        /// <summary>
        /// Adds a mod from HD.
        /// </summary>
        /// <param name="modPath">PAth to the mod.</param>
        /// <param name="modName">Name of the mod (leave blank for auto fill).</param>
        /// <param name="installAfterAdd">Flag that determines if the mod should be installed after adding to the ModSelection.</param>
        /// <returns>The new added mod (maybe null).</returns>
        public static ModNode HandleModAddViaPath(string modPath, string modName, bool installAfterAdd)
        {
            return HandleModAddViaModInfo(new ModInfo { LocalPath = modPath, Name = Path.GetFileNameWithoutExtension(modPath) }, installAfterAdd);

            //ModNode newMod = null;
            //List<ModNode> addedMods = AddMods(new ModInfo[] { new ModInfo { LocalPath = modPath, Name = Path.GetFileNameWithoutExtension(modPath) } }, true, null);
            //if (addedMods.Count > 0 && !string.IsNullOrEmpty(modName))
            //    addedMods[0].Text = modName;

            //if (installAfterAdd)
            //    ProcessMods(addedMods.ToArray());

            //if (addedMods.Count > 0)
            //    newMod = addedMods[0];

            //return newMod;
        }

        /// <summary>
        /// Adds the ModNodes to the mod selection tree.
        /// </summary>
        /// <param name="modNode">The nodes to add.</param>
        /// <param name="showCollisionDialog"></param>
        internal static List<ModNode> AddMods(ModNode[] modNode, bool showCollisionDialog = true)
        {
            List<ModNode> addedMods = new List<ModNode>();

            foreach (ModNode node in modNode)
            {
                try
                {
                    Model.AddMod(node);
                    ModNodeHandler.SetToolTips(node);
                    //ModNodeHandler.CheckNodesWithDestination(node);
                    addedMods.Add(node);

                }
                catch (Exception ex)
                {
                    MessageBox.Show(View, ex.Message, Messages.MSG_TITLE_ERROR);
                    Messenger.AddError(string.Format(Messages.MSG_ADD_MOD_FAILED_0, node.Name), ex);
                }
            }

            InvalidateView();

            return addedMods;
        }

        /// <summary>
        /// Adds a MOD to the TreeView.
        /// </summary>
        /// <param name="fileNames">Paths to the Zip-Files of the KSP mods.</param>
        /// <param name="showCollisionDialog">Flag to show/hide the collision dialog.</param>
        internal static void AddModsAsync(string[] fileNames, bool showCollisionDialog = true)
        {
            if (fileNames.Length > 0)
            {
                ModInfo[] modInfos = new ModInfo[fileNames.Length];
                for (int i = 0; i < fileNames.Length; ++i)
                    modInfos[i] = new ModInfo { LocalPath = fileNames[i], Name = Path.GetFileNameWithoutExtension(fileNames[i]) };

                AddModsAsync(modInfos, showCollisionDialog);
            }
            else
            {
                Messenger.AddError(Messages.MSG_ADD_MODS_FAILED_PARAM_EMPTY_FILENAMES);
            }
        }

        /// <summary>
        /// Creates nodes from the ModInfos and adds the nodes to the ModSelection.
        /// </summary>
        /// <param name="modInfos">The nodes to add.</param>
        /// <param name="showCollisionDialog"></param>
        internal static void AddModsAsync(ModInfo[] modInfos, bool showCollisionDialog = true)
        {
            if (modInfos.Length <= 0)
            {
                Messenger.AddError(Messages.MSG_ADD_MODS_FAILED_PARAM_EMPTY_MODINFOS);
                return;
            }

            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.SetProgressBarStates(true, modInfos.Length, 0);

            AsyncTask<List<ModNode>> asnyJob = new AsyncTask<List<ModNode>>();
            asnyJob.SetCallbackFunctions(() =>
                {
                    return AddMods(modInfos, showCollisionDialog, asnyJob);
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);
                },
                (percentage) =>
                {
                    View.SetProgressBarStates(true, modInfos.Length, percentage);
                });
            asnyJob.Run();
        }

        /// <summary>
        /// Creates nodes from the ModInfos and adds the nodes to the ModSelection.
        /// </summary>
        /// <param name="modInfos">The nodes to add.</param>
        /// <param name="showCollisionDialog"></param>
        internal static List<ModNode> AddMods(ModInfo[] modInfos, bool showCollisionDialog, AsyncTask<List<ModNode>> asyncJob = null)
        {
            int doneCount = 0;
            List<ModNode> addedMods = new List<ModNode>();

            foreach (ModInfo modInfo in modInfos)
            {
                Messenger.AddInfo(Constants.SEPARATOR);
                Messenger.AddInfo(string.Format(Messages.MSG_START_ADDING_0, modInfo.Name));
                Messenger.AddInfo(Constants.SEPARATOR);

                try
                {
                    // already added?
                    ModNode newNode = null;
                    ModNode mod = (string.IsNullOrEmpty(modInfo.ProductID)) ? null : Model[modInfo.ProductID, modInfo.SiteHandlerName];
                    if (mod == null && !Model.ContainsLocalPath(modInfo.LocalPath))
                    {
                        try
                        {
                            if (modInfo.LocalPath.ToLower().EndsWith(Constants.EXT_CRAFT) && File.Exists(modInfo.LocalPath))
                                modInfo.LocalPath = ModZipCreator.CreateZipOfCraftFile(modInfo.LocalPath);

                            newNode = ModNodeHandler.CreateModNode(modInfo);
                            if (newNode != null)
                            {
                                Model.AddMod(newNode);
                                Messenger.AddInfo(string.Format(Messages.MSG_MOD_ADDED_0, newNode.Text));
                            }
                        }
                        catch (Exception ex)
                        {
                            Messenger.AddError(string.Format(Messages.MSG_MOD_ERROR_WHILE_READ_ZIP_0_ERROR_MSG_1, string.Empty, ex.Message), ex);
                        }

                        View.InvokeIfRequired(() =>
                        {
                            if (OptionsController.ShowConflictSolver && showCollisionDialog && newNode != null &&
                                newNode.HasChildCollision)
                            {
                                MessageBox.Show(View, "ConflictSolver not Implemented yet!");
                                // TODO :
                                //frmCollisionSolving dlg = new frmCollisionSolving { CollisionMod = newNode };
                                //dlg.ShowDialog();
                            }
                        });
                    }
                    else if (mod != null && (mod.IsOutdated || modInfo.CreationDateAsDateTime > mod.CreationDateAsDateTime) &&
                             OptionsController.ModUpdateBehavior != ModUpdateBehavior.Manualy)
                    {
                        newNode = UpdateMod(modInfo, mod);
                    }
                    else
                    {
                        View.InvokeIfRequired(() =>
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine(string.Format(Messages.MSG_MOD_ALREADY_ADDED, modInfo.Name));
                            sb.AppendLine();
                            sb.AppendLine(Messages.MSG_SHOULD_MOD_REPLACED);
                            if (MessageBox.Show(View, sb.ToString(), Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.YesNo) ==
                                DialogResult.Yes)
                            {
                                ModNode outdatedMod = Model[modInfo.LocalPath];
                                Messenger.AddInfo(string.Format(Messages.MSG_REPLACING_MOD_0, outdatedMod.Text));

                                newNode = ModNodeHandler.CreateModNode(modInfo);
                                RemoveOutdatedAndAddNewMod(outdatedMod, newNode);

                                newNode.UncheckAll();

                                Messenger.AddInfo(string.Format(Messages.MSG_MOD_0_REPLACED, newNode.Text));

                                if (OptionsController.ShowConflictSolver && showCollisionDialog && newNode != null &&
                                    newNode.HasChildCollision)
                                {
                                    MessageBox.Show(View, "ConflictSolver not Implemented yet!");
                                    //frmCollisionSolving dlg = new frmCollisionSolving { CollisionMod = newNode };
                                    //dlg.ShowDialog();
                                }
                            }
                        });
                    }

                    if (newNode != null)
                        addedMods.Add(newNode);

                    newNode = null;

                    if (asyncJob != null)
                        asyncJob.PercentFinished = doneCount += 1;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(View, ex.Message, Messages.MSG_TITLE_ERROR);
                    Messenger.AddError(string.Format(Messages.MSG_ADD_MOD_FAILED_0, modInfo.Name), ex);
                }

                InvalidateView();

                Messenger.AddInfo(Constants.SEPARATOR);
            }

            return addedMods;
        }

        #endregion

        #region Processing mods

        /// <summary>
        /// Processes all nodes of the ModSelection. (Adds/Removes the mods to/from the KSP install folders).
        /// </summary>
        public static void ProcessAllModsAsync(bool silent = false)
        {
            ProcessModsAsync(Mods, silent);
        }

        /// <summary>
        /// Processes all passed nodes. (Adds/Removes the MOD to/from the KSP install folders).
        /// </summary>
        /// <param name="nodeArray">The NodeArray to process.</param>
        /// <param name="silent">Determines if info messages should be added displayed.</param>
        public static void ProcessMods(ModNode[] nodeArray, bool silent = false)
        {
            try
            {
                int maxNodeCount = 0;
                int nodeCount = 0;
                foreach (ModNode mod in nodeArray)
                {
                    maxNodeCount += ModSelectionTreeModel.GetFullNodeCount(new ModNode[] { mod });
                    nodeCount += ModNodeHandler.ProcessMod(mod, silent, View.OverrideModFiles, (i) => { View.SetProgressBarStates(true, maxNodeCount, i); }, nodeCount) - nodeCount;
                }
            }
            catch (Exception ex)
            {
                Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_PROCESSING_MOD_0, ex), ex);
            }
            finally
            {
                View.SetProgressBarStates(false);
                InvalidateView();
            }
        }

        /// <summary>
        /// Processes all passed nodes. (Adds/Removes the MOD to/from the KSP install folders).
        /// </summary>
        /// <param name="nodeArray">The NodeArray to process.</param>
        /// <param name="silent">Determines if info messages should be added displayed.</param>
        public static void ProcessModsAsync(ModNode[] nodeArray, bool silent = false)
        {
            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.SetProgressBarStates(true, 1, 0);

            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(() =>
            {
                ProcessMods(nodeArray, silent);

                return true;
            },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);

                    if (ex != null)
                    {
                        string msg = string.Format(Messages.MSG_ERROR_DURING_PROCESSING_MOD_0, ex.Message);
                        Messenger.AddError(msg, ex);
                        MessageBox.Show(View, msg, Messages.MSG_TITLE_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                });
            asyncJob.Run();
        }

        #endregion

        #region Removing Mod

        /// <summary>
        /// Clears the mod selection tree.
        /// </summary>
        public static void ClearMods()
        {
            Model.Nodes.Clear();
            ModRegister.Clear();
        }

        /// <summary>
        /// Uninstalls and removes the mods from the ModSelection.
        /// </summary>
        /// <param name="modsToRemove">The mods to remove.</param>
        /// <param name="silent">Flag to avoid pop up messages.</param>
        public static void RemoveMod(ModNode[] modsToRemove, bool silent = false)
        {
            if (modsToRemove == null || modsToRemove.Count() == 0)
                return;

            string msg = string.Empty;
            if (modsToRemove.Count() == 1)
                msg = string.Format(Messages.MSG_DELETE_MOD_0_QUESTION, modsToRemove[0].ToString());
            else
                msg = string.Format(Messages.MSG_DELETE_MODS_0_QUESTION, Environment.NewLine + string.Join<ModNode>(Environment.NewLine, modsToRemove));

            if (silent || DialogResult.Yes == MessageBox.Show(View.ParentForm, msg, Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.YesNo))
                RemoveModsAsync(modsToRemove, silent);
        }

        /// <summary>
        /// Uninstalls and removes all mods in the ModSelection.
        /// </summary>
        /// <param name="silent">Flag to avoid pop up messages.</param>
        public static void RemoveAllMods(bool silent = false)
        {
            if (silent || DialogResult.Yes == MessageBox.Show(View.ParentForm, Messages.MSG_DELETE_ALL_MODS_QUESTION, Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.YesNo))
                RemoveModsAsync(Mods, silent);
        }

        /// <summary>
        /// Uninstalls and removes the mods from the ModSelection.
        /// </summary>
        /// <param name="modsToRemove">The mods to remove.</param>
        protected static void RemoveModsAsync(ModNode[] modsToRemove, bool silent = false)
        {
            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.SetProgressBarStates(true, modsToRemove.Length, 0);

            int doneCount = 0;
            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(
                () =>
                {
                    foreach (ModNode mod in modsToRemove)
                    {
                        if (!silent)
                        {
                            Messenger.AddInfo(Constants.SEPARATOR);
                            Messenger.AddInfo(string.Format(Messages.MSG_REMOVING_MOD_0, mod.Name));
                            Messenger.AddInfo(Constants.SEPARATOR);
                        }

                        ModNode modToRemove = mod.ZipRoot;

                        try
                        {
                            // prepare to uninstall all mods
                            modToRemove.UncheckAll();

                            // uninstall all mods
                            ProcessMods(new ModNode[] { modToRemove }, silent);
                            ModRegister.RemoveRegisteredMod(modToRemove);

                            View.InvokeIfRequired(() => { Model.RemoveMod(modToRemove); });
                        }
                        catch (Exception ex)
                        {
                            Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REMOVING_MOD_0, modToRemove.Name), ex);
                        }

                        asyncJob.PercentFinished = doneCount++;

                        Messenger.AddInfo(string.Format(Messages.MSG_MOD_RMODVED_0, modToRemove.Name));

                        if (!silent)
                            Messenger.AddInfo(Constants.SEPARATOR);
                    }

                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);

                    InvalidateView();

                    MainController.SaveKSPConfig();
                },
                (percentage) => { View.SetProgressBarStates(true, modsToRemove.Length, percentage); });
            asyncJob.Run();
        }

        /// <summary>
        /// Removes the outdated mode from disk and ModSelection and adds the new mod to the ModSelection.
        /// </summary>
        /// <param name="outdatedMod">The mod to remove from ModSelection and disk.</param>
        /// <param name="newMod">The new mod to add to the ModSelection.</param>
        private static void RemoveOutdatedAndAddNewMod(ModNode outdatedMod, ModNode newMod)
        {
            Messenger.AddInfo(string.Format(Messages.MSG_REMOVING_OUTDATED_MOD_0, outdatedMod.Text));
            ModRegister.RemoveRegisteredMod(outdatedMod);
            outdatedMod.UncheckAll();
            ProcessMods(new ModNode[] { outdatedMod }, true);
            View.InvokeIfRequired(() => Model.RemoveMod(outdatedMod));

            Messenger.AddInfo(string.Format(Messages.MSG_ADDING_UPDATED_MOD_0, newMod.Text));
            Model.AddMod(newMod);
        }

        #endregion

        #region Edit/Copy ModInfos

        /// <summary>
        /// Opens the edit ModInfo dialog.
        /// </summary>
        public static void EditModInfos(ModNode modNode)
        {
            ModNode root = modNode.ZipRoot;
            frmEditModInfo dlg = new frmEditModInfo();
            dlg.ModZipRoot = root;
            if (dlg.ShowDialog(View.ParentForm) == DialogResult.OK)
            {
                if (!root.IsInstalled)
                    root.Text = dlg.ModName;

                root.AddDate = dlg.DownloadDate;
                root.Author = dlg.Author;
                root.CreationDate = dlg.CreationDate;
                root.Downloads = dlg.Downloads;
                root.Note = dlg.Note;
                root.ProductID = dlg.ProductID;
                root.SiteHandlerName = dlg.SiteHandlerName;
                root.ModURL = dlg.ModURL;
                root.AdditionalURL = dlg.AdditionalURL;
                root.Version = dlg.Version;

                InvalidateView();
            }
        }

        /// <summary>
        /// Opens the copy ModInfo dialog.
        /// </summary>
        /// <param name="modNode"></param>
        public static void CopyModInfos(ModNode modNode)
        {
            frmCopyModInfo dlg = new frmCopyModInfo();
            dlg.SourceMod = modNode.ZipRoot;
            dlg.Mods = Mods;
            dlg.ShowDialog(View.ParentForm);

            InvalidateView();
        }

        #endregion

        #region Destination

        /// <summary>
        /// Displays a dialog to select a new destination for the mod node.
        /// </summary>
        /// <param name="modNode">The mod node to change the destination.</param>
        public static void ChangeDestination(ModNode modNode)
        {
            if (modNode == null)
                return;

            if (modNode.IsInstalled || modNode.HasInstalledChilds)
                MessageBox.Show(View.ParentForm, Messages.MSG_FOLDER_INSTALLED_UNINSTALL_IT_TO_CHANGE_DESTINATION, Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            else
            {
                SelectDestinationFolder(modNode);
                InvalidateView();
            }
        }

        /// <summary>
        /// Resets the destination of the modNode and all its childs.
        /// </summary>
        /// <param name="modNode">The mod node to reset the destination.</param>
        public static void ResetDestination(ModNode modNode)
        {
            if (modNode == null)
                return;

            if (modNode.IsInstalled || modNode.HasInstalledChilds)
                MessageBox.Show(View.ParentForm, Messages.MSG_FOLDER_INSTALLED_UNINSTALL_IT_TO_CHANGE_DESTINATION, Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            else
            {
                ModNodeHandler.SetDestinationPaths(modNode, string.Empty);
                modNode.SetChecked(false);
                modNode.CheckAllChildes(false);
                InvalidateView();
            }
        }

        /// <summary>
        /// Displays a dialog to select a source and destination folder.
        /// </summary>
        /// <param name="node">The root node of the archive file.</param>
        /// <returns>True if dialog was quit with DialogResult.OK</returns>
        private static bool SelectDestinationFolder(ModNode node)
        {
            if (node == null) return false;

            string kspRootPath = KSPPathHelper.GetPath(KSPPaths.KSPRoot).ToLower();
            string dest = node.Destination.Replace(kspRootPath.ToLower(), string.Empty);
            if (dest.StartsWith("\\"))
                dest = dest.Substring(1);
            int index = dest.IndexOf("\\");
            if (index > -1)
                dest = dest.Substring(0, index);

            frmDestFolderSelection dlg = new frmDestFolderSelection();
            dlg.DestFolders = GetDefaultDestPaths();
            dlg.DestFolder = dest;
            dlg.SrcFolders = new ModNode[] { node };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                string src = dlg.SrcFolder;
                if (dlg.SrcFolder.Contains("/") || dlg.SrcFolder.Contains("\\"))
                    src = Path.GetFileName(dlg.SrcFolder);

                ModNode srcNode = ModSelectionTreeModel.SearchNode(src, node);
                if (srcNode == null)
                    srcNode = ModSelectionTreeModel.SearchNode(Path.GetFileNameWithoutExtension(src), node);

                if (srcNode != null)
                {
                    ModNodeHandler.SetDestinationPaths(srcNode, dlg.DestFolder, dlg.CopyContent);
                    InvalidateView();

                    if (OptionsController.ShowConflictSolver && node.HasChildCollision)
                    {
                        //TODO
                        //frmCollisionSolving csDlg = new frmCollisionSolving { CollisionMod = node };
                        //if (csDlg.ShowDialog() == DialogResult.OK && csDlg.SelectedMod != node.ZipRoot)
                        //    return false;
                    }

                    return true;
                }
                else
                    Messenger.AddInfo(Messages.MSG_SOURCE_NODE_NOT_FOUND);
            }

            return false;
        }

        /// <summary>
        /// Returns a string array of possible destination paths.
        /// </summary>
        /// <returns>A string array of possible destination paths.</returns>
        private static string[] GetDefaultDestPaths()
        {
            List<string> destFolders = new List<string>();
            destFolders.AddRange(Constants.KSPFolders);

            for (int i = 0; i < destFolders.Count<string>(); ++i)
                destFolders[i] = KSPPathHelper.GetPathByName(destFolders[i]);

            return destFolders.ToArray();
        }

        #endregion

        #region Scan GameData

        /// <summary>
        /// Scanns the KSP GameData directory for installed mods and adds them to the ModSelection.
        /// </summary>
        internal static void ScanGameData()
        {
            Messenger.AddDebug(Constants.SEPARATOR);
            Messenger.AddDebug(Messages.MSG_SCAN_GAMDATA_FOLDER_STARTED);
            Messenger.AddDebug(Constants.SEPARATOR);
            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.ShowBusy = true;

            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(() => 
                {
                    string[] ignoreDirs = new string[] { "squad", "myflags", "nasamission" };
                    List<ScanInfo> entries = new List<ScanInfo>();
                    try
                    {
                        string scanDir = KSPPathHelper.GetPath(KSPPaths.GameData);
                        string[] dirs = Directory.GetDirectories(scanDir);
                        foreach (string dir in dirs)
                        {
                            string dirname = dir.Substring(dir.LastIndexOf("\\") + 1);
                            if (!ignoreDirs.Contains(dirname.ToLower()))
                            {
                                Messenger.AddDebug(string.Format(Messages.MSG_DIRECTORY_0_FOUND, dirname));
                                ScanInfo scanInfo = new ScanInfo(dirname, dir, false);
                                entries.Add(scanInfo);
                                ScanDir(scanInfo);
                            }
                        }

                        List<ScanInfo> unknowns = GetUnknowenNodes(entries);
                        if (unknowns.Count > 0)
                        {
                            foreach (ScanInfo unknown in unknowns)
                            {
                                ModNode node = ScanInfoToKSPMA_TreeNode(unknown);
                                RefreshCheckedStateOfMod(node);
                                Model.Nodes.Add(node);
                                Messenger.AddInfo(string.Format(Messages.MSG_MOD_ADDED_0, node.Text));
                            }
                        }
                        else
                            Messenger.AddInfo(Messages.MSG_SCAN_NO_NEW_MODS_FOUND);
                    }
                    catch (Exception ex)
                    {
                        Messenger.AddError(Messages.MSG_SCAN_ERROR_DURING_SCAN, ex);
                    }

                    return true;
                },
                (result, ex) =>
                {
                    Messenger.AddDebug(Constants.SEPARATOR);

                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.ShowBusy = false;
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Scanns the passed dir for files and directories and creates a tree of ScannInfos from it.
        /// </summary>
        /// <param name="scanDir">The ScanInfo of the start directory.</param>
        private static void ScanDir(ScanInfo scanDir)
        {
            List<ScanInfo> entries = new List<ScanInfo>();
            foreach (string file in Directory.GetFiles(scanDir.Path))
            {
                Messenger.AddDebug(string.Format(Messages.MSG_FILE_0_FOUND, file));
                string filename = Path.GetFileName(file);
                ScanInfo scanInfo = new ScanInfo(filename, file, true, scanDir);
                scanInfo.Parent = scanDir;
            }

            string[] dirs = Directory.GetDirectories(scanDir.Path);
            foreach (string dir in dirs)
            {
                Messenger.AddDebug(string.Format(Messages.MSG_DIRECTORY_0_FOUND, dir));
                string dirname = dir.Substring(dir.LastIndexOf("\\") + 1);
                ScanInfo scanInfo = new ScanInfo(dirname, dir, false, scanDir);
                ScanDir(scanInfo);
            }
        }

        /// <summary>
        /// Searches the list of ScanInfo trees for unknowen nodes.
        /// Searches the complete ModSelection for a matching node.
        /// </summary>
        /// <param name="scanInfos">A list of ScanInfos trees to search.</param>
        /// <returns>A list of scanInfo trees with unknown nodes.</returns>
        private static List<ScanInfo> GetUnknowenNodes(List<ScanInfo> scanInfos)
        {
            List<ScanInfo> entries = new List<ScanInfo>();
            foreach (ScanInfo entry in scanInfos)
            {
                bool found = false;
                foreach (ModNode node in Mods)
                {
                    if (CompareNodes(entry, node))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    entries.Add(entry);
                else
                    Messenger.AddDebug(string.Format(Messages.MSG_SKIPPING_0, entry.Path));
            }
            return entries;
        }

        /// <summary>
        /// Compares the ScanInfo to all known nodes (from parent).
        /// </summary>
        /// <param name="scanInfo">The ScanInfo to compare.</param>
        /// <param name="parent">The start node of the comparision.</param>
        /// <returns>True if a match was found, otherwise false.</returns>
        private static bool CompareNodes(ScanInfo scanInfo, ModNode parent)
        {
            if (scanInfo.Name == parent.Text)
                return true;

            foreach (ModNode child in parent.Nodes)
            {
                if (child.Text == scanInfo.Name)
                    return true;

                if (CompareNodes(scanInfo, child))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a TreeNodeMod from the passed ScanInfo.
        /// </summary>
        /// <param name="unknown">The ScanInfo of the unknown node.</param>
        /// <returns>The new created TeeeNodeMod.</returns>
        private static ModNode ScanInfoToKSPMA_TreeNode(ScanInfo unknown)
        {
            ModNode node = new ModNode();
            node.Key = unknown.Path;
            node.Name = unknown.Name;
            node.AddDate = DateTime.Now.ToString();
            node.Destination = KSPPathHelper.GetRelativePath(unknown.Path);
            node.NodeType = (unknown.IsFile) ? NodeType.UnknownFileInstalled : NodeType.UnknownFolderInstalled;
            node._Checked = true;

            Messenger.AddDebug(string.Format(Messages.MSG_MODNODE_0_CREATED, node.Key));

            foreach (ScanInfo si in unknown.Childs)
            {
                ModNode child = ScanInfoToKSPMA_TreeNode(si);
                node.Nodes.Add(child);
            }

            return node;
        }

        #endregion

        #region Checked state

        #region Refresh CheckedStated

        /// <summary>
        /// Traversing the complete tree and renews the checked state of all nodes.
        /// </summary>
        public static void RefreshCheckedStateAllModsAsync()
        {
            ModNode[] allMods = Mods;

            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);

            int maxCount = ModSelectionTreeModel.GetFullNodeCount(allMods);
            View.SetProgressBarStates(true, maxCount, 0);

            int count = 0;
            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(
                () =>
                {
                    foreach (var mod in allMods)
                    {
                        Messenger.AddDebug(string.Format(Messages.MSG_REFRESHING_CHECKEDSTATE_0, mod.Name));
                        RefreshCheckedState(mod, ref count, asyncJob);
                    }

                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);
                    InvalidateView();

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REFRESH_CHECKED_STATE_0, ex.Message), ex);
                },
                (processedCount) =>
                {
                    View.SetProgressBarStates(true, maxCount, processedCount);
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Traversing the complete tree and renews the checked state of all nodes.
        /// </summary>
        public static void RefreshCheckedStateAllMods()
        {
            foreach (var mod in Mods)
                RefreshCheckedStateOfMod(mod);

            InvalidateView();
        }

        /// <summary>
        /// Traversing the complete tree and renews the checked state of all nodes.
        /// </summary>
        public static void RefreshCheckedStateOfModAsync(ModNode mod)
        {
            ModNode rootNode = mod.ZipRoot;
            Messenger.AddDebug(string.Format(Messages.MSG_REFRESHING_CHECKEDSTATE_0, rootNode.Name));

            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);

            int maxCount = ModSelectionTreeModel.GetFullNodeCount(new[] { rootNode });
            View.SetProgressBarStates(true, maxCount, 0);

            int count = 0;
            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(
                () =>
                {
                    RefreshCheckedState(rootNode, ref count, asyncJob);
                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REFRESH_CHECKED_STATE_0, ex.Message), ex);
                },
                (processedCount) =>
                {
                    View.SetProgressBarStates(true, maxCount, processedCount);
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Traversing the complete tree and renews the checked state of all nodes.
        /// </summary>
        public static void RefreshCheckedStateOfMod(ModNode mod)
        {
            Messenger.AddDebug(string.Format(Messages.MSG_REFRESHING_CHECKEDSTATE_0, mod.Name));
            int count = 0;
            RefreshCheckedState(mod.ZipRoot, ref count);
            InvalidateView();
        }

        /// <summary>
        /// Traversing the complete tree and renews the checked state of all nodes.
        /// </summary>
        protected static void RefreshCheckedState(ModNode mod, ref int processedCount, AsyncTask<bool> asyncJob = null)
        {
            try
            {
                processedCount += 1;
                if (asyncJob != null)
                    asyncJob.ProgressChanged(null, new ProgressChangedEventArgs(processedCount, null));

                bool isInstalled = false;
                NodeType nodeType = NodeType.UnknownFolder;

                if (!mod.HasDestination)
                {
                    isInstalled = false;
                    nodeType = (mod.IsFile) ? NodeType.UnknownFile : NodeType.UnknownFolder;
                }
                else
                {
                    isInstalled = mod.IsInstalled = mod.IsModNodeInstalled();

                    if (mod.IsFile)
                    {
                        //value = File.Exists(KSPPathHelper.GetDestinationPath(mod));
                        nodeType = (isInstalled) ? NodeType.UnknownFileInstalled : NodeType.UnknownFile;
                    }
                    else
                    {
                        //bool isInstalled = Directory.Exists(KSPPathHelper.GetDestinationPath(mod));
                        bool hasInstalledChilds = mod.HasInstalledChilds;

                        bool isKSPDir = false;
                        isKSPDir = KSPPathHelper.IsKSPDir(KSPPathHelper.GetAbsolutePath(mod));

                        if (isKSPDir)
                        {
                            isInstalled = (isInstalled && hasInstalledChilds);
                            nodeType = (isInstalled) ? NodeType.KSPFolderInstalled : NodeType.KSPFolder;
                        }
                        else
                        {
                            isInstalled = (isInstalled || hasInstalledChilds);
                            nodeType = (isInstalled) ? NodeType.UnknownFolderInstalled : NodeType.UnknownFolder;
                        }
                    }
                }

                View.InvokeIfRequired(() =>
                {
                    mod._Checked = isInstalled;
                    mod.NodeType = nodeType;
                });
            }
            catch (Exception ex)
            {
                mod._Checked = false;
                Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REFRESH_CHECKED_STATE_0, ex.Message), ex);
            }

            foreach (ModNode child in mod.Nodes)
                RefreshCheckedState(child, ref processedCount, asyncJob);
        }

        #endregion

        /// <summary>
        /// Unchecks all Mods from the ModSelection.
        /// </summary>
        public static void UncheckAllMods()
        {
            Messenger.AddDebug(Messages.MSG_UNCHECKING_ALL_MODS);

            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);

            int maxCount = Mods.Length;
            View.SetProgressBarStates(true, maxCount, 0);

            int count = 0;
            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(
                () =>
                {
                    foreach (ModNode mod in Mods)
                    {
                        Messenger.AddDebug(string.Format(Messages.MSG_UNCHECKING_MOD_0, mod.Name));
                        asyncJob.ProgressChanged(null, new ProgressChangedEventArgs(++count, null));
                        View.InvokeIfRequired(() => { mod.Checked = false; } );
                    }

                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);

                    InvalidateView();

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REFRESH_CHECKED_STATE_0, ex.Message), ex);
                },
                (processedCount) =>
                {
                    View.SetProgressBarStates(true, maxCount, processedCount);
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Checks all Mods from the ModSelection.
        /// </summary>
        public static void CheckAllMods()
        {
            Messenger.AddDebug(Messages.MSG_CHECKING_ALL_MODS);

            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);

            int maxCount = Mods.Length;
            View.SetProgressBarStates(true, maxCount, 0);

            int count = 0;
            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(
                () =>
                {
                    foreach (ModNode mod in Mods)
                    {
                        Messenger.AddDebug(string.Format(Messages.MSG_CHECKING_MOD_0, mod.Name));
                        asyncJob.ProgressChanged(null, new ProgressChangedEventArgs(++count, null));
                        View.InvokeIfRequired(() => { mod.Checked = true; });
                    }

                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.SetProgressBarStates(false);

                    InvalidateView();

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_REFRESH_CHECKED_STATE_0, ex.Message), ex);
                },
                (processedCount) =>
                {
                    View.SetProgressBarStates(true, maxCount, processedCount);
                });
            asyncJob.Run();
        }

        #endregion

        #region Mod update

        /// <summary>
        /// Checks each mod of the ModSelection for updates.
        /// </summary>
        public static void CheckForUpdatesAllMods()
        {
            _CheckForModUpdates(Mods);
        }

        /// <summary>
        /// Checks each mod of the ModSelection for updates.
        /// </summary>
        public static void CheckForUpdatesAllModsAsync()
        {
            CheckForModUpdatesAsync(Mods);
        }

        /// <summary>
        /// Checks each mod for updates.
        /// </summary>
        /// <param name="mods">Array of mods to check for updates.</param>
        public static void CheckForModUpdates(ModNode[] mods)
        {
            _CheckForModUpdates(mods);
        }

        /// <summary>
        /// Checks each mod for updates.
        /// </summary>
        /// <param name="mods">Array of mods to check for updates.</param>
        public static void CheckForModUpdatesAsync(ModNode[] mods)
        {
            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.ShowBusy = true;

            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(() =>
                {
                    _CheckForModUpdates(mods);
                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.ShowBusy = false;

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_MOD_UPDATE_0, ex.Message), ex);
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Checks each mod for updates.
        /// </summary>
        /// <param name="mods">Array of mods to check for updates.</param>
        protected static void _CheckForModUpdates(ModNode[] mods)
        {
            foreach (ModNode mod in mods)
            {
                try
                {
                    ISiteHandler siteHandler = mod.SiteHandler;
                    if (siteHandler == null)
                        Messenger.AddInfo(string.Format(Messages.MSG_ERROR_0_NO_VERSIONCONTROL, mod.Name));
                    else
                    {
                        ModInfo newModinfo = null;
                        Messenger.AddInfo(string.Format(Messages.MSG_UPDATECHECK_FOR_MOD_0_VIA_1, mod.Name, mod.SiteHandlerName));
                        if (!siteHandler.CheckForUpdates(mod.ModInfo, ref newModinfo))
                            Messenger.AddInfo(string.Format(Messages.MSG_MOD_0_IS_UPTODATE, mod.Name));
                        else
                        {
                            Messenger.AddInfo(string.Format(Messages.MSG_MOD_0_IS_OUTDATED, mod.Name));
                            mod.IsOutdated = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Messenger.AddInfo(string.Format(Messages.MSG_ERROR_DURING_UPDATECHECK_0_ERRORMSG_1, mod.Name, ex.Message));
                }
            }
        }

        /// <summary>
        /// Starts a update check for all mods and updates all outdated mods.
        /// </summary>
        public static void UpdateAllOutdatedMods()
        {
            _UpdateOutdatedMods(Mods);
        }

        /// <summary>
        /// Starts a update check for all mods and updates all outdated mods.
        /// </summary>
        public static void UpdateAllOutdatedModsAsync()
        {
            UpdateOutdatedModsAsync(Mods);
        }

        /// <summary>
        /// Starts a update check for the mod and updates it if it's outdated.
        /// </summary>
        /// <param name="mods">The mod of the mod to update.</param>
        public static void UpdateOutdatedMods(ModNode[] mods)
        {
            _UpdateOutdatedMods(mods);
        }

        /// <summary>
        /// Starts a update check for the mod and updates it if it's outdated.
        /// </summary>
        /// <param name="mods">The mod of the mod to update.</param>
        public static void UpdateOutdatedModsAsync(ModNode[] mods)
        {
            EventDistributor.InvokeAsyncTaskStarted(Instance);
            View.SetEnabledOfAllControls(false);
            View.ShowBusy = true;

            AsyncTask<bool> asyncJob = new AsyncTask<bool>();
            asyncJob.SetCallbackFunctions(() =>
                {
                    _UpdateOutdatedMods(mods);
                    return true;
                },
                (result, ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                    View.ShowBusy = false;

                    if (ex != null)
                        Messenger.AddError(string.Format(Messages.MSG_ERROR_DURING_MOD_UPDATE_0, ex.Message), ex);
                });
            asyncJob.Run();
        }

        /// <summary>
        /// Starts a update check for the mod and updates it if it's outdated.
        /// </summary>
        /// <param name="mods">The mod of the mod to update.</param>
        protected static void _UpdateOutdatedMods(ModNode[] mods)
        {
            _CheckForModUpdates(mods);

            var outdatedMods = from e in Mods where e.IsOutdated select e;
            foreach (ModNode mod in outdatedMods)
            {
                try
                {
                    if (mod.IsOutdated)
                    {
                        var handler = mod.SiteHandler;
                        if (handler != null)
                        {
                            Messenger.AddInfo(string.Format(Messages.MSG_DOWNLOADING_MOD_0, mod.Name));
                            ModInfo newModInfos = handler.GetModInfo(mod.ModURL);
                            if (handler.DownloadMod(ref newModInfos))
                                UpdateMod(newModInfos, mod);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Messenger.AddInfo(string.Format(Messages.MSG_ERROR_DURING_MODUPDATE_0_ERROR_1, mod.Name, ex.Message));
                }
            }
        }

        /// <summary>
        /// Updates the outdated mod.
        /// Tries to copy the checked state and destination of a mod and its parts, then uninstalls the outdated mod and installs the new one.
        /// </summary>
        /// <param name="newModInfo">The ModeInfo of the new mod.</param>
        /// <param name="outdatedMod">The root ModNode of the outdated mod.</param>
        public static ModNode UpdateMod(ModInfo newModInfo, ModNode outdatedMod)
        {
            ModNode newMod = null;
            try
            {
                Messenger.AddInfo(string.Format(Messages.MSG_UPDATING_MOD_0, outdatedMod.Text));
                newMod = ModNodeHandler.CreateModNode(newModInfo);
                if (OptionsController.ModUpdateBehavior == ModUpdateBehavior.RemoveAndAdd || (!outdatedMod.IsInstalled && !outdatedMod.HasInstalledChilds))
                {
                    RemoveOutdatedAndAddNewMod(outdatedMod, newMod);
                    View.InvokeIfRequired(() => { newMod._Checked = false; });
                }
                else
                {
                    // Find matching file nodes and copy destination from old to new mod.
                    if (ModNodeHandler.TryCopyDestToMatchingNodes(outdatedMod, newMod))
                    {
                        newMod.ModURL = outdatedMod.ModURL;
                        newMod.AdditionalURL = outdatedMod.AdditionalURL;
                        newMod.Note = outdatedMod.Note;
                        //View.InvokeIfRequired(() =>
                        //{
                        RemoveOutdatedAndAddNewMod(outdatedMod, newMod);
                        ProcessMods(new ModNode[] { newMod }, true);
                        //});
                    }
                    else
                    {
                        // No match found -> user must handle update.
                        View.InvokeIfRequired(() => MessageBox.Show(View.ParentForm, string.Format(Messages.MSG_ERROR_UPDATING_MOD_0_FAILED, outdatedMod.Text)));
                    }

                    View.InvokeIfRequired(() =>
                    {
                        if (OptionsController.ShowConflictSolver && newMod != null && newMod.HasChildCollision)
                        {
                            MessageBox.Show(View, "ConflictSolver not Implemented yet!");
                            // TODO :
                            //frmCollisionSolving dlg = new frmCollisionSolving();
                            //dlg.CollisionMod = newMod;
                            //dlg.ShowDialog();
                        }
                    });
                }

                Messenger.AddInfo(string.Format(Messages.MSG_MOD_0_UPDATED, newMod.Text));
            }
            catch (Exception ex)
            {
                Messenger.AddError(string.Format(Messages.MSG_ERROR_WHILE_UPDATING_MOD_0_ERROR_1, outdatedMod.Text, ex.Message), ex);
            }

            return newMod;


            //MessageBox.Show(View, string.Format("Mod \"{0}\" is outdated.{1}BUT: Auto update is not implemented yet!", mod.Name, Environment.NewLine),
            //    Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);

            //return null;
        }

        #endregion

        #region ModPack export/import

        /// <summary>
        /// Opens the ModPack Import/Export dialog.
        /// </summary>
        public static void OpenExportImportDialog()
        {
            frmImExport dlg = new frmImExport();
            dlg.ShowDialog(View.ParentForm);
        }

        #endregion

        #region Mod zipping

        /// <summary>
        /// Creates a zip for each root node in the passed node list.
        /// </summary>
        /// <param name="nodes">List of root nodes to create zips for.</param>
        public static void CreateZip(List<ModNode> nodes)
        {
            // get path for the zip
            if (!Directory.Exists(OptionsController.DownloadPath))
                OptionsController.SelectNewDownloadPath();

            if (!Directory.Exists(OptionsController.DownloadPath))
            {
                Messenger.AddInfo(Messages.MSG_ERROR_NO_DOWNLOAD_FOLDER_SELECTED);
                Messenger.AddInfo(Messages.MSG_ZIP_CREATION_ABORTED);
                return;
            }

            int nodeCount = ModSelectionTreeModel.GetFullNodeCount(nodes);

            // disable controls
            View.SetEnabledOfAllControls(false);
            EventDistributor.InvokeAsyncTaskStarted(Instance);

            AsyncTask<bool>.DoWork(
                () =>
                {
                    try
                    {
                        return ModZipCreator.CreateZip(nodes, OptionsController.DownloadPath);
                    }
                    catch (Exception ex)
                    {
                        View.InvokeIfRequired(() => MessageBox.Show(View.ParentForm, string.Format(Messages.MSG_ZIP_CREATION_FAILED_0, ex.Message), Messages.MSG_TITLE_ERROR, MessageBoxButtons.OK, MessageBoxIcon.Error));
                        return false;
                    }
                },
                (bool b, Exception ex) =>
                {
                    EventDistributor.InvokeAsyncTaskDone(Instance);
                    View.SetEnabledOfAllControls(true);
                },
                (int processedNodeCount) =>
                {
                    View.SetProgressBarStates(true, nodeCount, processedNodeCount);
                });
        }

        #endregion

        /// <summary>
        /// Sorts the nodes of the ModSelection depending on the passed SortType.
        /// </summary>
        /// <param name="sortType">Determines the property to use for the sort.</param>
        /// <param name="desc">Determines if the sorting should be descending or ascending.</param>
        public static void SortModSelection(SortType sortType = SortType.ByName, bool desc = true)
        {
            // move or redirect to Model.ModSelectionTreeModel.SortModSelection

            // TODO: implementation
            InvalidateView();
        }

        /// <summary>
        /// Opens the ConflictSolver dilaog.
        /// </summary>
        public static void OpenConflictSolver()
        {
            MessageBox.Show(View.ParentForm, "Not implemented yet!", Messages.MSG_TITLE_ATTENTION, MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
        }
    }
}
