package tv.amis.pamynx.ksp.civpop;

import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.function.Supplier;
import java.util.stream.Collectors;
import java.util.stream.IntStream;

import org.apache.poi.ss.usermodel.Cell;
import org.apache.poi.ss.usermodel.Row;
import org.apache.poi.ss.usermodel.Sheet;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import tv.amis.pamynx.ksp.civpop.beans.KspConfig;
import tv.amis.pamynx.ksp.civpop.beans.KspConfigField;

public class KspListConfigLoader<F extends KspConfigField, C extends KspConfig<F>> {
	
	private final Logger logger = LoggerFactory.getLogger(this.getClass());

	private Supplier<C> creator;
	
	public KspListConfigLoader(Supplier<C> creator) {
		super();
		this.creator = creator;
	}
	
	public Map<String, List<C>> loadFrom(Sheet sheet) {
		Map<String, Integer> cellIndexes = this.readSheetIndexes(sheet);
		
		Map<String, List<C>> configs = IntStream.range(1, sheet.getLastRowNum()+1)
			.mapToObj(i -> sheet.getRow(i))
			.filter(row -> row != null)
			.map(row -> buildFrom(row, cellIndexes))
			.filter(config -> config.getName() != null)
			.collect(Collectors.groupingBy(config ->config.getName()));
		return configs;
	}
	
	protected Map<String, Integer> readSheetIndexes(Sheet sheet) {
		Row row = sheet.getRow(0);
		Map<String, Integer> res = new HashMap<>();
		for (int cellIndex = 0; cellIndex < row.getLastCellNum(); cellIndex++) {
			res.put(row.getCell(cellIndex).getStringCellValue(), cellIndex);
		}
		return res;
	}

	private C buildFrom(Row row, Map<String, Integer> cellIndexes) {
		C c = creator.get();
		for (F f : c.values()) {
			try {
				Cell cell = row.getCell(cellIndexes.get(f.name()));
				if (cell != null) {
					switch (cell.getCellTypeEnum()) {
					case STRING :
						c.set(f, cell.getStringCellValue());					
						break;
					case NUMERIC :
						double value = cell.getNumericCellValue();
						if (value == Math.floor(value)) {
							c.set(f, String.valueOf((int)value));
						} else {
							c.set(f, String.valueOf(value));
						}
						break;
					case BLANK :
						break;
					default:
						throw new ConfigBuilderException(new IllegalStateException("Bad cell type : "+cell.getCellTypeEnum().name()));
					}
				}
			} catch(Throwable t) {
				logger.error("error on field "+f.name()+" : "+t.getMessage());
				throw new ConfigBuilderException(t);
			}
		}
		return c;
	}

}
