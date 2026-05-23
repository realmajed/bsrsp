using BeanSceneReservationSystemProject.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeanSceneReservationSystemProject.Controllers
{
    [Authorize(Roles = "Manager,Owner")]
    public class SittingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SittingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: SittingsController
        public async Task<IActionResult> Index()
        {
            var sittings = await _context.Sittings
                .Include(s => s.Reservations)
                .OrderBy(s => s.StartDateTime)
                .ToListAsync();

            return View(sittings);
        }

        // GET: SittingsController/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var sitting = await _context.Sittings
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.SittingId == id);
            if (sitting == null) return NotFound();

            return View(sitting);
        }

        // GET: SittingsController/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: SittingsController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Sitting sitting)
        {
            AddSittingValidationErrors(sitting);
            if (ModelState.IsValid)
            {
                _context.Add(sitting);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Sitting created.";
                return RedirectToAction(nameof(Index));
            }

            return View(sitting);
        }

        // GET: SittingsController/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var sitting = await _context.Sittings.FindAsync(id);
            if (sitting == null) return NotFound();
            return View(sitting);
        }

        // POST: SittingsController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Sitting sitting)
        {
            if (id != sitting.SittingId) return NotFound();
            AddSittingValidationErrors(sitting);
            if (ModelState.IsValid)
            {
                // Do not let capacity drop below existing non-cancelled bookings.
                var bookedGuests = await _context.Reservations
                    .Where(r => r.SittingId == id && r.Status != ReservationStatus.Cancelled)
                    .SumAsync(r => r.NumberOfGuests);

                if (sitting.Capacity < bookedGuests)
                {
                    ModelState.AddModelError(nameof(sitting.Capacity), $"Capacity cannot be less than the {bookedGuests} guests already booked.");
                    return View(sitting);
                }

                _context.Update(sitting);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Sitting updated.";
                return RedirectToAction(nameof(Index));
            }

            return View(sitting);
        }

        // GET: SittingsController/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var sitting = await _context.Sittings
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.SittingId == id);
            if (sitting == null) return NotFound();

            return View(sitting);
        }

        // POST: SittingsController/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var sitting = await _context.Sittings
                .Include(s => s.Reservations)
                .FirstOrDefaultAsync(s => s.SittingId == id);
            if (sitting == null)
            {
                return RedirectToAction(nameof(Index));
            }

            if (sitting.Reservations.Any())
            {
                TempData["ErrorMessage"] = "This sitting has reservation history and cannot be deleted. Close it instead.";
                return RedirectToAction(nameof(Index));
            }

            _context.Sittings.Remove(sitting);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Sitting deleted.";
            return RedirectToAction(nameof(Index));
        }

        private void AddSittingValidationErrors(Sitting sitting)
        {
            // Keep sitting times sane before EF saves them.
            if (sitting.EndDateTime <= sitting.StartDateTime)
            {
                ModelState.AddModelError(nameof(sitting.EndDateTime), "End time must be after the start time.");
            }

            if ((sitting.EndDateTime - sitting.StartDateTime).TotalMinutes < 30)
            {
                ModelState.AddModelError(nameof(sitting.EndDateTime), "A sitting must run for at least 30 minutes.");
            }
        }
    }
}
