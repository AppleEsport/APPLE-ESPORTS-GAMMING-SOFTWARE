import { jsPDF } from 'jspdf';
import autoTable from 'jspdf-autotable';

const PAGE_MARGIN = 40;
const ACCENT_RGB = [255, 51, 102]; // matches --accent brand color used across the app
const TEXT_DARK = [30, 30, 35];
const TEXT_MUTED = [110, 110, 120];

// Light status-tint backgrounds for conditional row highlighting (e.g. credit status).
export const ROW_TINT_RED = [253, 226, 226];
export const ROW_TINT_GREEN = [220, 245, 230];
// Neutral tint for a section-header row (e.g. one shift's heading inside a table grouped by
// shift) - deliberately not red or green, since a header row is not a status.
export const ROW_TINT_NEUTRAL = [228, 232, 240];

function pageWidth(doc) {
  return doc.internal.pageSize.getWidth();
}

/**
 * Creates a new landscape A4 jsPDF doc with a title header drawn on the first page.
 * Returns { doc, cursorY } where cursorY is the Y position right below the header.
 */
export function createReport({ title, subtitle }) {
  const doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
  drawHeader(doc, title, subtitle);
  return { doc, cursorY: 90 };
}

function drawHeader(doc, title, subtitle) {
  const w = pageWidth(doc);
  doc.setFont('helvetica', 'bold');
  doc.setFontSize(16);
  doc.setTextColor(...ACCENT_RGB);
  doc.text('APPLE ESPORTS', PAGE_MARGIN, 36);

  doc.setFontSize(13);
  doc.setTextColor(...TEXT_DARK);
  doc.text(title, PAGE_MARGIN, 58);

  if (subtitle) {
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(9);
    doc.setTextColor(...TEXT_MUTED);
    doc.text(subtitle, PAGE_MARGIN, 74);
  }

  doc.setFont('helvetica', 'normal');
  doc.setFontSize(8);
  doc.setTextColor(...TEXT_MUTED);
  const generatedAt = `Generated: ${new Date().toLocaleString('en-IN')}`;
  doc.text(generatedAt, w - PAGE_MARGIN, 36, { align: 'right' });

  doc.setDrawColor(...ACCENT_RGB);
  doc.setLineWidth(1);
  doc.line(PAGE_MARGIN, 82, w - PAGE_MARGIN, 82);
}

/**
 * Draws a row of label/value stat cards (2-4 per row) starting at y.
 * stats: [{ label, value }]
 * Returns the new Y position.
 */
export function addStatGrid(doc, y, stats) {
  if (!stats?.length) return y;
  const w = pageWidth(doc);
  const usableWidth = w - PAGE_MARGIN * 2;
  const colWidth = usableWidth / stats.length;

  stats.forEach((stat, i) => {
    const x = PAGE_MARGIN + i * colWidth;
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(8);
    doc.setTextColor(...TEXT_MUTED);
    doc.text(String(stat.label).toUpperCase(), x, y);

    doc.setFont('helvetica', 'bold');
    doc.setFontSize(14);
    doc.setTextColor(...TEXT_DARK);
    doc.text(String(stat.value), x, y + 20);
  });

  return y + 40;
}

/**
 * Draws a section heading followed by an autoTable. Automatically flows to a new
 * page if it doesn't fit, and repeats the brand header on any page it spills onto.
 * Returns the new Y position (below the table) for the next section to start at.
 */
export function addTable(doc, y, { heading, head, body, title, subtitle, rowColor }) {
  const w = pageWidth(doc);
  const pageHeight = doc.internal.pageSize.getHeight();

  // If there's not even room for the heading + a couple of rows, start a fresh page.
  if (y > pageHeight - 120) {
    doc.addPage();
    drawHeader(doc, title, subtitle);
    y = 90;
  }

  if (heading) {
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(10);
    doc.setTextColor(...ACCENT_RGB);
    doc.text(heading, PAGE_MARGIN, y);
    y += 12;
  }

  autoTable(doc, {
    startY: y,
    head: [head],
    body,
    styles: { fontSize: 7.5, cellPadding: 4, textColor: TEXT_DARK, lineColor: [225, 225, 230] },
    headStyles: { fillColor: ACCENT_RGB, textColor: [255, 255, 255], fontStyle: 'bold' },
    alternateRowStyles: { fillColor: [248, 248, 250] },
    theme: 'grid',
    didParseCell: (data) => {
      if (rowColor && data.section === 'body') {
        const color = rowColor(data.row.index);
        if (color) data.cell.styles.fillColor = color;
      }
    },
    didDrawPage: () => {
      // Repeat the brand header whenever autoTable itself paginates mid-table.
      if (doc.internal.getCurrentPageInfo().pageNumber > 1) {
        drawHeader(doc, title, subtitle);
      }
    },
    margin: { top: 90, left: PAGE_MARGIN, right: PAGE_MARGIN },
  });

  return doc.lastAutoTable.finalY + 28;
}

export function save(doc, filename) {
  doc.save(filename);
}
