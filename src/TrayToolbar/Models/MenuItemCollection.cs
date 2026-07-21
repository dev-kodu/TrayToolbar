using System.Collections.ObjectModel;

using TrayToolbar.Extensions;
using TrayToolbar.Services;

namespace TrayToolbar.Models;

public class MenuItemCollection : ObservableCollection<ToolStripMenuItem>
{
    public MenuItemCollection(
        TrayToolbarConfiguration config,
        ToolStripItemClickedEventHandler clickHandler,
        MouseEventHandler mouseDownHandler
        )
    {
        Configuration = config;
        ClickHandler = clickHandler;
        MouseDownHandler = mouseDownHandler;
    }
    public bool NeedsRefresh { get; set; } = true;
    public TrayToolbarConfiguration Configuration { get; set; }
    public ToolStripItemClickedEventHandler ClickHandler { get; set; }
    public MouseEventHandler MouseDownHandler { get; set; }

    internal void LoadFolderMenu(FolderConfig folder)
    {
        Clear();
        PopulateDirectoryEntries(folder.Name!.ToLocalPath(), folder, null);
        NeedsRefresh = false;
    }

    internal void CreateMenuItem(
        string file,
        FolderConfig folder,
        string menuPath = "")
    {
        if (menuPath.Length > Configuration.MaxMenuPath) return; //guard against long paths or infinite loops
        var parentPath = Path.GetDirectoryName(file);
        var folderPath = folder.Name!.ToLocalPath();
        if (parentPath.HasValue() && !parentPath.Is(folderPath))
        {
            if (Configuration.IgnoreAllDotFiles && parentPath.Contains(@"\."))
                return; //it's in a dot folder like .git or it's a dot file
            if (Configuration.IgnoreFolders.Contains(Path.GetFileName(parentPath)))
                return; //it's in an ignored folder name
        }
        var relativePath = Path.GetRelativePath(folderPath, parentPath ?? folderPath);
        if (relativePath == ".") relativePath = "";
        var scanner = new FolderScanner(ConfigHelper.FileSystem);
        if (scanner.TryResolveFolderLinkTarget(file, Configuration, out var targetPath))
        {
            var submenuName = Path.GetFileNameWithoutExtension(file);
            var subfolder = folder.WithPath(targetPath);
            var newRoot = Path.Combine(menuPath, relativePath, submenuName);
            foreach (var f in EnumerateFiles(targetPath, folder.Recursive, Configuration))
            {
                CreateMenuItem(f, subfolder, newRoot);
            }
            return; // Skip adding the link itself, as it's now a submenu
        }
        var menuName = (Configuration.HideFileExtensions || file.FileExtension().IsOneOf(".lnk", ".url"))
            ? Path.GetFileNameWithoutExtension(file)
            : Path.GetFileName(file);

        var parent = GetParent(menuPath, relativePath, folderPath);
        if (parent == null)
        {
            // If the item already exists, don't add it again
            if (!this.Any(i => i.Name == menuName))
            {
                Insert(IndexOfItem(menuName, AccessibleRole.MenuItem), CreateMenuEntry(file, menuName));
            }
        }
        // If the item already exists, don't add it again
        else if (!parent.DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Name == menuName))
        {
            parent.DropDownItems.Insert(IndexOfItem(parent.DropDownItems, menuName, AccessibleRole.MenuItem), CreateMenuEntry(file, menuName));
        }
    }

    private ToolStripMenuItem? GetParent(string path, string relativePath, string folderPath)
    {
        if (!path.HasValue() && !relativePath.HasValue()) return null;
        var parts = Path.Combine(path, relativePath).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        ToolStripMenuItem? parent = null;
        var tag = "";
        var directory = new List<string>(relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in parts)
        {
            tag = Path.Combine(tag, part);
            folderPath = Path.Combine(folderPath, directory.Pop() ?? "");
            if (parent == null)
            {
                parent = this.FirstOrDefault(i => $"{i.Tag}" == tag);
                if (parent == null)
                {
                    parent = CreateSubMenu(tag, part, folderPath);
                    Insert(IndexOfItem(parent.Text!, AccessibleRole.MenuPopup), parent);
                }
            }
            else
            {
                var submenu = parent.DropDownItems
                    .OfType<ToolStripMenuItem>()
                    .FirstOrDefault(i => $"{i.Tag}" == tag);
                if (submenu == null)
                {
                    submenu = CreateSubMenu(tag, part, folderPath);
                    parent.DropDownItems.Insert(IndexOfItem(parent.DropDownItems, submenu.Text!, AccessibleRole.MenuPopup), submenu);
                }
                parent = submenu;
            }
        }
        return parent;
    }

    private ToolStripMenuItem CreateSubMenu(string tag, string name, string target)
    {
        var menu = new ToolStripMenuItem
        {
            Name = name,
            Tag = tag,
            Text = name.ToMenuName(),
            CommandParameter = target,
            Image = target.GetImage(Configuration.LargeIcons),
            ImageScaling = ToolStripItemImageScaling.None,
            AccessibleRole = AccessibleRole.MenuPopup,
        };
        if (Configuration.ShowToolTips) menu.ToolTipText = target;

        menu.MouseDown += MouseDownHandler;
        menu.Click += MenuClicked(menu);
        menu.DropDownOpening += Menu_DropDownOpening;
        return menu;
    }

    private void Menu_DropDownOpening(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem menuItem)
        {
            EnsureSubMenuLoaded(menuItem);

            if (!menuItem.HasDropDownItems)
            {
                return;
            }

            var parent = menuItem.GetCurrentParent();
            if (parent != null)
            {
                var bounds = parent.Bounds;
                var currentScreen = Screen.FromPoint(bounds.Location);
                var maxWidth = 0;
                foreach (ToolStripMenuItem subitem in menuItem.DropDownItems)
                {
                    maxWidth = Math.Max(subitem.Width, maxWidth);
                }
                maxWidth += 10;

                var end = bounds.Right + maxWidth;
                var currentMonitorRight = currentScreen.Bounds.Right;

                menuItem.DropDownDirection = end > currentMonitorRight
                    ? ToolStripDropDownDirection.Left
                    : ToolStripDropDownDirection.Right;
            }
        }
    }

    private EventHandler MenuClicked(ToolStripMenuItem menu)
    {
        var eventArgs = new ToolStripItemClickedEventArgs(menu);
        return (s, e) => ClickHandler.Invoke(s, eventArgs);
    }

    internal static IEnumerable<string> EnumerateFiles(string path, bool recursive, TrayToolbarConfiguration config)
    {
        return new FolderScanner(ConfigHelper.FileSystem).EnumerateFiles(path, recursive, config);
    }

    private void PopulateDirectoryEntries(string directoryPath, FolderConfig folder, ToolStripMenuItem? parentMenu)
    {
        var scanner = new FolderScanner(ConfigHelper.FileSystem);
        foreach (var entry in ConfigHelper.FileSystem.EnumerateFileSystemEntries(directoryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (ConfigHelper.FileSystem.DirectoryExists(entry))
            {
                if (!folder.Recursive || ShouldIgnoreDirectory(entry, folder.Name!.ToLocalPath()))
                {
                    continue;
                }

                var submenuName = Path.GetFileName(entry);
                AddMenuItem(parentMenu, CreateSubMenu(entry, submenuName, entry));
                continue;
            }

            if (scanner.TryResolveFolderLinkTarget(entry, Configuration, out var targetPath))
            {
                var submenuName = Path.GetFileNameWithoutExtension(entry);
                AddMenuItem(parentMenu, CreateSubMenu(targetPath, submenuName, targetPath));
                continue;
            }

            if (!Configuration.IncludesFile(entry))
            {
                continue;
            }

            var menuName = (Configuration.HideFileExtensions || entry.FileExtension().IsOneOf(".lnk", ".url"))
                ? Path.GetFileNameWithoutExtension(entry)
                : Path.GetFileName(entry);
            AddMenuItem(parentMenu, CreateMenuEntry(entry, menuName));
        }
    }

    private void EnsureSubMenuLoaded(ToolStripMenuItem menuItem)
    {
        if (menuItem.DropDownItems.Count > 0)
        {
            return;
        }

        var targetPath = $"{menuItem.CommandParameter}";
        if (!ConfigHelper.FileSystem.DirectoryExists(targetPath))
        {
            return;
        }

        var folder = new FolderConfig
        {
            Name = targetPath,
            Recursive = true,
        };
        PopulateDirectoryEntries(targetPath, folder, menuItem);
    }

    private void AddMenuItem(ToolStripMenuItem? parentMenu, ToolStripMenuItem menuItem)
    {
        if (parentMenu == null)
        {
            if (this.Any(i => i.Name == menuItem.Name && i.AccessibleRole == menuItem.AccessibleRole))
            {
                return;
            }

            Insert(IndexOfItem(menuItem.Name ?? string.Empty, menuItem.AccessibleRole), menuItem);
            return;
        }

        if (parentMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(i => i.Name == menuItem.Name && i.AccessibleRole == menuItem.AccessibleRole))
        {
            return;
        }

        parentMenu.DropDownItems.Insert(IndexOfItem(parentMenu.DropDownItems, menuItem.Name ?? string.Empty, menuItem.AccessibleRole), menuItem);
    }

    private ToolStripMenuItem CreateMenuEntry(string file, string menuName)
    {
        var entry = new ToolStripMenuItem
        {
            Name = menuName,
            Text = menuName.ToMenuName(),
            CommandParameter = file,
            Image = file.GetImage(Configuration.LargeIcons),
            ImageScaling = ToolStripItemImageScaling.None,
            AccessibleRole = AccessibleRole.MenuItem
        };
        entry.Click += MenuClicked(entry);
        entry.MouseDown += MouseDownHandler;
        if (Configuration.ShowToolTips) entry.ToolTipText = file;
        return entry;
    }

    private bool ShouldIgnoreDirectory(string path, string rootPath)
    {
        if (Configuration.IgnoreFolders.Contains(Path.GetFileName(path)))
        {
            return true;
        }

        if (!Configuration.IgnoreAllDotFiles)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(rootPath, path);
        return relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.StartsWith('.'));
    }

    private static int IndexOfItem(ToolStripItemCollection list, string text, AccessibleRole role)
    {
        var i = 0;
        foreach (ToolStripMenuItem entry in list)
        {
            if (entry.AccessibleRole == role)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(entry.Name, text) > 0) break;
                i++;
            }
            else if (role == AccessibleRole.MenuItem)
            {
                i++;
            }
        }
        return i;
    }

    private int IndexOfItem(string text, AccessibleRole role)
    {
        var i = 0;
        foreach (var entry in this)
        {
            if (entry.AccessibleRole == role)
            {
                if (StringComparer.OrdinalIgnoreCase.Compare(entry.Name, text) > 0) break;
                i++;
            }
            else if (role == AccessibleRole.MenuItem)
            {
                i++;
            }
        }
        return i;
    }

    internal bool DeleteMenu(string fullPath)
    {
        foreach (var item in this)
        {
            if ($"{item.CommandParameter}".Is(fullPath))
            {
                Remove(item);
                return true;
            }
            if (DeleteMenu(fullPath, item.DropDownItems)) return true;
        }
        return false;
    }

    private static bool DeleteMenu(string fullPath, ToolStripItemCollection dropDownItems)
    {
        foreach (ToolStripMenuItem submenu in dropDownItems)
        {
            if ($"{submenu.CommandParameter}".Is(fullPath))
            {
                dropDownItems.Remove(submenu);
                return true;
            }
            if (DeleteMenu(fullPath, submenu.DropDownItems)) return true;
        }
        return false;
    }
}
