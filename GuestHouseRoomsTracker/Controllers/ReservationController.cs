using DNBarbershop.Models.EnumClasses;
using GuestHouseRoomsTracker.Core.IServices;
using GuestHouseRoomsTracker.Core.Services;
using GuestHouseRoomsTracker.Models.Entities;
using GuestHouseRoomsTracker.Models.Reservations;
using GuestHouseRoomsTracker.Models.Room;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace GuestHouseRoomsTracker.Controllers
{
    public class ReservationController : Controller
    {
        private readonly IReservationService _reservationService;
        private readonly IRoomService _roomService;
        public ReservationController(IReservationService reservationService,IRoomService roomService)
        {
            _reservationService = reservationService;
            _roomService = roomService;
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(ReservationFilterViewModel filter)
        {
            if (filter == null)
            {
                filter = new ReservationFilterViewModel();
            }

            
            var reservations = _reservationService.GetAll()
            .Include(r => r.Room)
            .AsQueryable();

            if (filter.CheckInDate != null)
            {
                reservations = reservations.Where(r => r.CheckInDate >= filter.CheckInDate.Value);
            }

            if (filter.CheckOutDate != null)
            {
                reservations = reservations.Where(r => r.CheckOutDate <= filter.CheckOutDate.Value);
            }

            var reservationList = await reservations.ToListAsync();
            
            foreach (var item in reservationList)
            {
                var originalStatus = item.Status;

                if (item.CheckOutDate.Date <= DateTime.Now.Date && item.Status != DNBarbershop.Models.EnumClasses.ReservationStatus.Cancelled)
                {
                    item.Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Completed;
                }
                else if (item.CheckInDate<=DateTime.Now.Date && item.CheckOutDate>DateTime.Now.Date && item.Status != DNBarbershop.Models.EnumClasses.ReservationStatus.Cancelled)
                {
                    item.Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Current;
                }
                else if (item.Status == DNBarbershop.Models.EnumClasses.ReservationStatus.Cancelled)
                {
                    item.Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Cancelled;
                }
                else
                {
                    item.Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Scheduled;
                }
                if (originalStatus != item.Status)
                {
                    await _reservationService.Update(item); 
                }
            }

            var model = new ReservationFilterViewModel
            {
                CheckInDate = filter.CheckInDate,
                CheckOutDate = filter.CheckOutDate,
                Reservations = await reservations.OrderBy(r => r.Status).ThenBy(r => r.CheckInDate).ThenBy(r => r.CheckOutDate).ToListAsync()
            };
            
            return View(model);
        }
        //[Authorize(Roles = "Admin")]
        //[HttpGet]
        //public IActionResult Index()
        //{
        //    var reservations = _reservationService.GetAll()
        //        .Include(r => r.Room)
        //        .OrderBy(r => r.CheckInDate).ThenBy(r => r.CheckOutDate)
        //        .ToList();

        //    var model = new ReservationFilterViewModel
        //    {
        //        Reservations = reservations
        //    };

        //    return View(model);
        //}
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Add()
        {
            var model = new ReservationCreateViewModel();

            model.CheckInDate = DateTime.Today;
            model.CheckOutDate = DateTime.Today.AddDays(1);
            model.Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Scheduled;
            var rooms = _roomService.GetAll().ToList();
            if (!rooms.Any())
            {
                TempData["error"] = "Няма налични стаи.";
                return RedirectToAction("Index");
            }
            model.Rooms = rooms.ToList();
            return View(model);
        }
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Add(ReservationCreateViewModel model)
        {
            model.Rooms = _roomService.GetAll().ToList();

            if (!model.Rooms.Any())
            {
                TempData["error"] = "Няма налични стаи.";
                return RedirectToAction("Index");
            }

            if (model.CheckOutDate <= model.CheckInDate)
            {
                TempData["error"] = "Датата на напускане трябва да е след датата на настаняване.";
                return View(model);
            }

            var room = await _roomService.GetAll()
                .FirstOrDefaultAsync(r => r.RoomNumber == model.RoomNumber);

            if (room == null)
            {
                TempData["error"] = "Стая с такъв номер не съществува.";
                return View(model);
            }

            bool conflictExists = await _reservationService.GetAll()
                .AnyAsync(r => r.RoomId == room.Id &&
                        (r.CheckInDate < model.CheckOutDate &&
                        r.CheckOutDate > model.CheckInDate));

            if (conflictExists)
            {
                TempData["error"] = $"Стая {room.RoomNumber} е резервирана за част от избрания период. Моля изберете други дати.";
                return View(model);
            }
            var reservation = new Reservation
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                GuestName = model.GuestName,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                PhoneNumber = model.PhoneNumber,
                Notes = model.Notes,
                CreatedAt = DateTime.Now,
                Status = DNBarbershop.Models.EnumClasses.ReservationStatus.Scheduled
            };

            await _reservationService.CreateReservationAsync(reservation);
            TempData["success"] = "Резервацията е създадена успешно!";
            return RedirectToAction("Index");
        }
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(Guid id)
        {
            var reservation = await _reservationService.Get(x => x.Id == id);
            if (reservation == null)
            {
                TempData["error"] = "Резервацията не е намерена.";
                return RedirectToAction("Index");
            }

            var rooms = _roomService.GetAll().ToList();
            var room = rooms.FirstOrDefault(r => r.Id == reservation.RoomId);

            ViewBag.Statuses = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "Предстояща"},
                new SelectListItem { Value = "3", Text = "Отменена"}
            };

            var viewModel = new ReservationEditViewModel
            {
                Id = reservation.Id,
                GuestName = reservation.GuestName,
                PhoneNumber = reservation.PhoneNumber,
                CheckInDate = reservation.CheckInDate,
                CheckOutDate = reservation.CheckOutDate,
                RoomNumber = room.RoomNumber,
                Notes = reservation.Notes,
                Rooms = rooms,
                Status = reservation.Status
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(ReservationEditViewModel model)
        {
            var reservation = await _reservationService.Get(x => x.Id == model.Id);
            if (reservation == null)
            {
                TempData["error"] = "Резервацията не е намерена.";
                return RedirectToAction("Index");
            }

            if (model.CheckOutDate <= model.CheckInDate)
            {
                TempData["error"] = "Датата на напускане трябва да е след датата на настаняване.";
                model.Rooms = _roomService.GetAll().ToList();
                return View(model);
            }

            if (model.CheckInDate < DateTime.Today)
            {
                TempData["error"] = "Датата на настаняване не може да бъде в миналото.";
                model.Rooms = _roomService.GetAll().ToList();
                return View(model);
            }

            var room = _roomService.GetAll().FirstOrDefault(r => r.RoomNumber == model.RoomNumber);
            if (room == null)
            {
                TempData["error"] = "Стая с такъв номер не съществува.";
                model.Rooms = _roomService.GetAll().ToList();
                return View(model);
            }

            var existingReservations = _reservationService.GetAll().ToList();
            foreach (var existingReservation in existingReservations)
            {
                if (existingReservation.RoomId == room.Id &&
                    (existingReservation.CheckInDate < model.CheckOutDate && existingReservation.CheckOutDate > model.CheckInDate) &&
                    existingReservation.Status != DNBarbershop.Models.EnumClasses.ReservationStatus.Cancelled && existingReservation.Id != model.Id)
                {
                    TempData["error"] = $"Стая {room.RoomNumber} е резервирана за част от избрания период. Моля изберете други дати.";

                    ViewBag.Statuses = new List<SelectListItem>
                    {
                        new SelectListItem { Value = "1", Text = "Предстояща"},
                        new SelectListItem { Value = "3", Text = "Отменена"}
                    };
                    model.Rooms = _roomService.GetAll().ToList();
                    return View(model);
                }
            }

            //var newReservation = new Reservation
            //{
            //    RoomId = room.Id,
            //    GuestName = model.GuestName,
            //    PhoneNumber = model.PhoneNumber,
            //    CheckInDate = model.CheckInDate,
            //    CheckOutDate = model.CheckOutDate,
            //    Notes = model.Notes,
            //    Status = model.Status
            //};

            reservation.Id = model.Id; 
            reservation.RoomId = room.Id;
            reservation.GuestName = model.GuestName;
            reservation.PhoneNumber = model.PhoneNumber;
            reservation.CheckInDate = model.CheckInDate;
            reservation.CheckOutDate = model.CheckOutDate;
            reservation.Notes = model.Notes;
            reservation.Status = model.Status;
            


            await _reservationService.Update(reservation);

            TempData["success"] = "Резервацията беше успешно редактирана.";
            return RedirectToAction("Index");
        }
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        [HttpPost]
        public async Task<IActionResult> Delete(Guid id)
        {
            var res = await _reservationService.Get(r => r.Id == id);
            if (res == null)
            {
                TempData["error"] = "Резервацията не е намерена.";
                return RedirectToAction("Index");
            }

            try
            {
                await _reservationService.Delete(id);
                TempData["success"] = "Резервацията е изтрита успешно.";
            }
            catch
            {
                TempData["error"] = "Грешка при изтриване на резервацията.";
            }

            return RedirectToAction("Index");
        }
    }
}
