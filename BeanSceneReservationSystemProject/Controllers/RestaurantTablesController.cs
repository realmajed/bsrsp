using BeanSceneReservationSystemProject.Models;
using BeanSceneReservationSystemProject.Services;
using BeanSceneReservationSystemProject.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BeanSceneReservationSystemProject.Controllers
{
    [Authorize(Roles = "Owner,Manager")]
    public class RestaurantTablesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public RestaurantTablesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var viewModel = new TableManagementViewModel
            {
                Tables = TableOrdering.OrderTables(await _context.RestaurantTables.Include(t => t.Area).ToListAsync()),
                Areas = await _context.Areas
                    .Include(a => a.RestaurantTables)
                    .OrderBy(a => a.AreaName)
                    .ToListAsync()
            };

            return View(viewModel);
        }

        public ActionResult Create()
        {
            LoadAreaDropdown();
            return View(new RestaurantTable { IsAvailable = true, Capacity = 4 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RestaurantTable table)
        {
            await AddTableValidationErrorsAsync(table);

            if (ModelState.IsValid)
            {
                _context.RestaurantTables.Add(table);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Table created.";
                return RedirectToAction(nameof(Index));
            }

            LoadAreaDropdown(table.AreaId);
            return View(table);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var table = await _context.RestaurantTables.FindAsync(id);
            if (table == null) return NotFound();

            LoadAreaDropdown(table.AreaId);
            return View(table);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, RestaurantTable table)
        {
            if (id != table.RestaurantTableId) return NotFound();

            await AddTableValidationErrorsAsync(table);

            if (ModelState.IsValid)
            {
                _context.Update(table);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Table updated.";
                return RedirectToAction(nameof(Index));
            }

            LoadAreaDropdown(table.AreaId);
            return View(table);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var table = await _context.RestaurantTables
                .Include(t => t.Area)
                .FirstOrDefaultAsync(t => t.RestaurantTableId == id);
            if (table == null) return NotFound();

            return View(table);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var table = await _context.RestaurantTables.FindAsync(id);
            if (table == null) return RedirectToAction(nameof(Index));

            var hasReservations = await _context.ReservationTables.AnyAsync(rt => rt.RestaurantTableId == id);
            if (hasReservations)
            {
                TempData["ErrorMessage"] = "This table has reservation history and cannot be deleted. Mark it unavailable instead.";
                return RedirectToAction(nameof(Index));
            }

            _context.RestaurantTables.Remove(table);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Table deleted.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateArea(Area newArea)
        {
            await AddAreaValidationErrorsAsync(newArea);

            if (ModelState.IsValid)
            {
                _context.Areas.Add(newArea);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Area created.";
                return RedirectToAction(nameof(Index));
            }

            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> EditArea(int? id)
        {
            if (id == null) return NotFound();

            var area = await _context.Areas.FindAsync(id);
            if (area == null) return NotFound();

            return View(area);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditArea(int id, Area area)
        {
            if (id != area.AreaId) return NotFound();

            await AddAreaValidationErrorsAsync(area);

            if (ModelState.IsValid)
            {
                _context.Update(area);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Area updated.";
                return RedirectToAction(nameof(Index));
            }

            return View(area);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteArea(int id)
        {
            var area = await _context.Areas
                .Include(a => a.RestaurantTables)
                .FirstOrDefaultAsync(a => a.AreaId == id);
            if (area == null) return RedirectToAction(nameof(Index));

            if (area.RestaurantTables.Any())
            {
                TempData["ErrorMessage"] = "Move or delete the tables in this area before deleting it.";
                return RedirectToAction(nameof(Index));
            }

            _context.Areas.Remove(area);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Area deleted.";
            return RedirectToAction(nameof(Index));
        }

        private void LoadAreaDropdown(int? selectedAreaId = null)
        {
            ViewData["AreaId"] = new SelectList(_context.Areas.OrderBy(a => a.AreaName).ToList(), "AreaId", "AreaName", selectedAreaId);
        }

        private async Task AddTableValidationErrorsAsync(RestaurantTable table)
        {
            if (!await _context.Areas.AnyAsync(a => a.AreaId == table.AreaId))
            {
                ModelState.AddModelError(nameof(table.AreaId), "Choose a valid area.");
            }

            table.TableCode = table.TableCode.Trim();

            var duplicateExists = await _context.RestaurantTables.AnyAsync(t =>
                t.RestaurantTableId != table.RestaurantTableId &&
                t.TableCode == table.TableCode);

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(table.TableCode), "A table with this code already exists.");
            }
        }

        private async Task AddAreaValidationErrorsAsync(Area area)
        {
            area.AreaName = area.AreaName.Trim();

            var duplicateExists = await _context.Areas.AnyAsync(a =>
                a.AreaId != area.AreaId &&
                a.AreaName == area.AreaName);

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(area.AreaName), "An area with this name already exists.");
            }
        }

    }
}
